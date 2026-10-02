#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenRA.Primitives;
using OpenRA.Video;

namespace OpenRA.Mods.Dr.FileFormats
{
	/// <summary>
	/// A Smacker video (SMK2 or SMK4), as Dark Reign's menus and movies use: 8-bit frames in 4x4 blocks whose
	/// types, colours and maps are Huffman coded, a palette updated by deltas, and DPCM audio.
	///
	/// The header is 104 bytes: signature, width, height, frames, frame rate (positive milliseconds, negative
	/// tens of microseconds, zero 10 fps), flags (1 an extra ring frame), seven audio track sizes, the size of
	/// the trees, the four trees' table sizes, seven audio track rates (bit 31 compressed, 30 present, 29 16-bit,
	/// 28 stereo, the low 24 the sample rate), and a dummy. Then each frame's size (its low two bits flags),
	/// each frame's type (bit 0 a palette, bits 1-7 an audio track each), the trees, and the frames.
	///
	/// Bits are read from the least significant up. A byte tree is a 1 bit and two subtrees, or a 0 bit and
	/// an 8-bit leaf. The four video trees (block maps, block colours, full blocks, block types) are each a
	/// 1 bit, a byte tree for the low and the high byte, three 16-bit escapes, and a tree of leaves made of a
	/// code from each byte tree. Leaves equal to an escape hold the last three values decoded, most recent first.
	/// </summary>
	public sealed class SmackerVideo : IVideo, IDisposable
	{
		const int Node = 1 << 30;

		static readonly int[] BlockRuns =
		[
			1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
			17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
			33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48,
			49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 128, 256, 512, 1024, 2048
		];

		readonly Stream stream;
		readonly bool smk4;
		readonly bool useFramePadding;
		readonly long[] frameOffsets;
		readonly int[] frameSizes;
		readonly byte[] frameTypes;
		readonly uint audioFlags;

		readonly Tree mapTree, colourTree, fullTree, typeTree;

		readonly byte[] palette = new byte[768];
		readonly byte[] lastPalette = new byte[768];
		byte[] frameData;
		byte[] bgra;
		bool bgraValid;
		byte[] audio;

		public ushort Width { get; }
		public ushort Height { get; }
		public ushort FrameCount { get; }

		/// <summary>Frames a second, which need not be whole: the movies run at 15, a frame every 66.67 ms.</summary>
		public float FramesPerSecond { get; }

		public byte Framerate => (byte)Math.Clamp((int)Math.Round(FramesPerSecond), 1, 255);

		/// <summary>The current frame's palette indices, a byte a pixel, row after row.</summary>
		public byte[] Pixels { get; }

		/// <summary>The current frame's palette, opaque.</summary>
		public Color[] Palette { get; } = new Color[256];

		public int CurrentFrameIndex { get; private set; }

		public SmackerVideo(Stream stream, bool useFramePadding = false)
		{
			this.stream = stream;
			this.useFramePadding = useFramePadding;

			var header = stream.ReadBytes(104);
			var signature = Encoding.ASCII.GetString(header, 0, 4);
			if (signature != "SMK2" && signature != "SMK4")
				throw new InvalidDataException("Not a Smacker video");

			smk4 = signature == "SMK4";
			var width = BitConverter.ToInt32(header, 4);
			var height = BitConverter.ToInt32(header, 8);
			var frames = BitConverter.ToInt32(header, 12);
			var rate = BitConverter.ToInt32(header, 16);
			var flags = BitConverter.ToInt32(header, 20);
			var treesSize = BitConverter.ToInt32(header, 52);
			audioFlags = BitConverter.ToUInt32(header, 72);

			if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || frames <= 0 || frames > ushort.MaxValue)
				throw new InvalidDataException($"Smacker video of {width}x{height}, {frames} frames");

			Width = (ushort)width;
			Height = (ushort)height;
			FrameCount = (ushort)frames;
			FramesPerSecond = rate > 0 ? 1000f / rate : rate < 0 ? 100000f / -rate : 10f;

			// A ring frame, the first again for looping, follows the last; a loop here restarts instead.
			var stored = frames + ((flags & 1) != 0 ? 1 : 0);
			var sizes = stream.ReadBytes(4 * stored);
			frameTypes = stream.ReadBytes(stored);
			var trees = stream.ReadBytes(treesSize);

			frameSizes = new int[stored];
			frameOffsets = new long[stored];
			var offset = stream.Position;
			for (var i = 0; i < stored; i++)
			{
				frameSizes[i] = BitConverter.ToInt32(sizes, 4 * i) & ~3;
				frameOffsets[i] = offset;
				offset += frameSizes[i];
			}

			var bits = new BitReader(trees, 0, trees.Length);
			mapTree = Tree.ReadVideoTree(ref bits);
			colourTree = Tree.ReadVideoTree(ref bits);
			fullTree = Tree.ReadVideoTree(ref bits);
			typeTree = Tree.ReadVideoTree(ref bits);

			Pixels = new byte[width * height];
			Reset();
		}

		public void Reset()
		{
			Array.Clear(Pixels);
			Array.Clear(palette);
			CurrentFrameIndex = 0;
			DecodeFrame(0);
		}

		public void AdvanceFrame()
		{
			if (CurrentFrameIndex + 1 >= FrameCount)
				return;

			DecodeFrame(++CurrentFrameIndex);
		}

		/// <summary>Moves to a frame, decoding those between; going back starts again from the first.</summary>
		public void SeekTo(int frame)
		{
			frame = Math.Clamp(frame, 0, FrameCount - 1);
			if (frame < CurrentFrameIndex)
				Reset();

			while (CurrentFrameIndex < frame)
				AdvanceFrame();
		}

		byte[] ReadFrame(int frame)
		{
			var size = frameSizes[frame];
			if (frameData == null || frameData.Length < size)
				frameData = new byte[size];

			stream.Position = frameOffsets[frame];
			stream.ReadExactly(frameData, 0, size);
			return frameData;
		}

		void DecodeFrame(int frame)
		{
			var data = ReadFrame(frame);
			var size = frameSizes[frame];
			var p = 0;

			if ((frameTypes[frame] & 1) != 0 && size > 0)
			{
				var length = data[0] * 4;
				DecodePalette(data, 1, Math.Min(length, size) - 1);
				p = length;
			}

			for (var track = 0; track < 7; track++)
			{
				if ((frameTypes[frame] & (2 << track)) != 0 && p + 4 <= size)
					p += Math.Max(4, BitConverter.ToInt32(data, p));
			}

			if (p < size)
				DecodeVideo(data, p, size);

			for (var i = 0; i < 256; i++)
				Palette[i] = Color.FromArgb(255, palette[3 * i], palette[3 * i + 1], palette[3 * i + 2]);

			bgraValid = false;
		}

		/// <summary>
		/// Each byte: 1nnnnnnn keeps n + 1 entries, 01nnnnnn and an offset copies n + 1 entries of the frame
		/// before's palette from there, else six bits each of red, green and blue are a new entry.
		/// </summary>
		void DecodePalette(byte[] data, int p, int length)
		{
			Array.Copy(palette, lastPalette, 768);
			var end = Math.Min(data.Length, p + length);
			var entry = 0;
			while (entry < 256 && p < end)
			{
				int t = data[p++];
				if ((t & 0x80) != 0)
					entry += (t & 0x7F) + 1;
				else if ((t & 0x40) != 0)
				{
					if (p >= end)
						break;

					int from = data[p++];
					for (var n = (t & 0x3F) + 1; n > 0 && entry < 256 && from < 256; n--, entry++, from++)
						Array.Copy(lastPalette, 3 * from, palette, 3 * entry, 3);
				}
				else
				{
					if (p + 2 > end)
						break;

					palette[3 * entry] = Expand(t);
					palette[3 * entry + 1] = Expand(data[p++] & 0x3F);
					palette[3 * entry + 2] = Expand(data[p++] & 0x3F);
					entry++;
				}
			}
		}

		/// <summary>Six bits to eight, as Smacker's own table does.</summary>
		static byte Expand(int six) => (byte)(six * 4 + (six >> 4));

		void DecodeVideo(byte[] data, int start, int end)
		{
			var bits = new BitReader(data, start, end);
			mapTree.ResetRecent();
			colourTree.ResetRecent();
			fullTree.ResetRecent();
			typeTree.ResetRecent();

			var w = Width;
			var blocksWide = w / 4;
			var blocks = blocksWide * (Height / 4);
			var pixels = Pixels;
			var block = 0;
			while (block < blocks)
			{
				var type = typeTree.Decode(ref bits);
				var run = BlockRuns[(type >> 2) & 0x3F];
				switch (type & 3)
				{
					// Two colours and a 16-bit map of which pixel takes the high one.
					case 0:
						for (; run > 0 && block < blocks; run--, block++)
						{
							var colours = colourTree.Decode(ref bits);
							var map = mapTree.Decode(ref bits);
							var hi = (byte)(colours >> 8);
							var lo = (byte)colours;
							var o = block / blocksWide * 4 * w + block % blocksWide * 4;
							for (var y = 0; y < 4; y++, o += w, map >>= 4)
							{
								pixels[o] = (map & 1) != 0 ? hi : lo;
								pixels[o + 1] = (map & 2) != 0 ? hi : lo;
								pixels[o + 2] = (map & 4) != 0 ? hi : lo;
								pixels[o + 3] = (map & 8) != 0 ? hi : lo;
							}
						}

						break;

					// Every pixel, two at a time; Smacker 4 also has blocks of 2x2 or 2x1 pixels.
					case 1:
						var mode = 0;
						if (smk4)
							mode = bits.ReadBit() != 0 ? 1 : bits.ReadBit() != 0 ? 2 : 0;

						for (; run > 0 && block < blocks; run--, block++)
						{
							var o = block / blocksWide * 4 * w + block % blocksWide * 4;
							if (mode == 0)
							{
								for (var y = 0; y < 4; y++, o += w)
								{
									var right = fullTree.Decode(ref bits);
									var left = fullTree.Decode(ref bits);
									pixels[o] = (byte)left;
									pixels[o + 1] = (byte)(left >> 8);
									pixels[o + 2] = (byte)right;
									pixels[o + 3] = (byte)(right >> 8);
								}
							}
							else if (mode == 1)
							{
								for (var half = 0; half < 2; half++)
								{
									var pair = fullTree.Decode(ref bits);
									for (var y = 0; y < 2; y++, o += w)
									{
										pixels[o] = pixels[o + 1] = (byte)pair;
										pixels[o + 2] = pixels[o + 3] = (byte)(pair >> 8);
									}
								}
							}
							else
							{
								for (var half = 0; half < 2; half++)
								{
									var right = fullTree.Decode(ref bits);
									var left = fullTree.Decode(ref bits);
									for (var y = 0; y < 2; y++, o += w)
									{
										pixels[o] = (byte)left;
										pixels[o + 1] = (byte)(left >> 8);
										pixels[o + 2] = (byte)right;
										pixels[o + 3] = (byte)(right >> 8);
									}
								}
							}
						}

						break;

					// Unchanged from the frame before.
					case 2:
						block += run;
						break;

					// One colour, the type's high byte.
					case 3:
						var colour = (byte)(type >> 8);
						for (; run > 0 && block < blocks; run--, block++)
						{
							var o = block / blocksWide * 4 * w + block % blocksWide * 4;
							for (var y = 0; y < 4; y++, o += w)
								pixels[o] = pixels[o + 1] = pixels[o + 2] = pixels[o + 3] = colour;
						}

						break;
				}
			}
		}

		/// <summary>The current frame in 32-bit BGRA, its rows a power of two wide when the loader asked for padding.</summary>
		public byte[] CurrentFrameData
		{
			get
			{
				var stride = useFramePadding ? Exts.NextPowerOf2(Width) : Width;
				var rows = useFramePadding ? Exts.NextPowerOf2(Height) : Height;
				if (bgra == null)
					bgra = new byte[4 * stride * rows];

				if (!bgraValid)
				{
					for (var y = 0; y < Height; y++)
					{
						var o = 4 * y * stride;
						var s = y * Width;
						for (var x = 0; x < Width; x++, o += 4)
						{
							var c = Palette[Pixels[s + x]];
							bgra[o] = c.B;
							bgra[o + 1] = c.G;
							bgra[o + 2] = c.R;
							bgra[o + 3] = 255;
						}
					}

					bgraValid = true;
				}

				return bgra;
			}
		}

		public bool HasAudio => (audioFlags & 0x40000000) != 0;
		public int AudioChannels => (audioFlags & 0x10000000) != 0 ? 2 : 1;
		public int SampleBits => (audioFlags & 0x20000000) != 0 ? 16 : 8;
		public int SampleRate => (int)(audioFlags & 0xFFFFFF);

		/// <summary>The first audio track, all of it, decoded on first use.</summary>
		public byte[] AudioData
		{
			get
			{
				if (audio == null && HasAudio)
					audio = DecodeAudio();

				return audio;
			}
		}

		byte[] DecodeAudio()
		{
			var output = new MemoryStream();
			var compressed = (audioFlags & 0x80000000) != 0;
			var position = stream.Position;
			for (var frame = 0; frame < FrameCount; frame++)
			{
				if ((frameTypes[frame] & 2) == 0)
					continue;

				var data = ReadFrame(frame);
				var size = frameSizes[frame];
				var p = (frameTypes[frame] & 1) != 0 ? data[0] * 4 : 0;
				if (p + 4 > size)
					continue;

				var length = Math.Min(BitConverter.ToInt32(data, p), size - p) - 4;
				if (length <= 0)
					continue;

				if (compressed)
					DecodeAudioChunk(data, p + 4, length, output);
				else
					output.Write(data, p + 4, length);
			}

			stream.Position = position;
			return output.ToArray();
		}

		/// <summary>
		/// The unpacked size, then bits: data present, stereo, 16-bit; a byte tree for each byte of each channel;
		/// each channel's first sample (the right first; a 16-bit one high byte first); then each sample's change
		/// from the one before, coded by those trees, the channels interleaved. Sums wrap rather than clip.
		/// </summary>
		void DecodeAudioChunk(byte[] data, int p, int length, Stream output)
		{
			if (length <= 4)
				return;

			var unpacked = BitConverter.ToInt32(data, p);
			var bits = new BitReader(data, p + 4, p + length);
			if (bits.ReadBit() == 0)
				return;

			var stereo = bits.ReadBit();
			var sixteen = bits.ReadBit() != 0;
			var trees = new Tree[1 << ((sixteen ? 1 : 0) + stereo)];
			for (var i = 0; i < trees.Length; i++)
			{
				bits.ReadBit();
				trees[i] = Tree.ReadByteTree(ref bits);
				bits.ReadBit();
			}

			var samples = new byte[unpacked];
			if (sixteen)
			{
				Span<int> predicted = stackalloc int[2];
				for (var c = stereo; c >= 0; c--)
				{
					var v = bits.ReadBits(16);
					predicted[c] = (short)(((v & 0xFF) << 8) | (v >> 8));
				}

				var n = 0;
				for (var c = 0; c <= stereo && 2 * n + 1 < samples.Length; c++, n++)
					WriteShort(samples, n, predicted[c]);

				for (; 2 * n + 1 < samples.Length; n++)
				{
					var c = n & stereo;
					var lo = trees[2 * c].Decode(ref bits);
					var hi = trees[2 * c + 1].Decode(ref bits);
					predicted[c] = (short)(predicted[c] + (lo | (hi << 8)));
					WriteShort(samples, n, predicted[c]);
				}
			}
			else
			{
				Span<int> predicted = stackalloc int[2];
				for (var c = stereo; c >= 0; c--)
					predicted[c] = bits.ReadBits(8);

				var n = 0;
				for (var c = 0; c <= stereo && n < samples.Length; c++, n++)
					samples[n] = (byte)predicted[c];

				for (; n < samples.Length; n++)
				{
					var c = n & stereo;
					predicted[c] = (byte)(predicted[c] + (sbyte)trees[c].Decode(ref bits));
					samples[n] = (byte)predicted[c];
				}
			}

			output.Write(samples, 0, samples.Length);
		}

		static void WriteShort(byte[] samples, int n, int value)
		{
			samples[2 * n] = (byte)value;
			samples[2 * n + 1] = (byte)(value >> 8);
		}

		public void Dispose()
		{
			stream.Dispose();
		}

		/// <summary>Bits from the least significant of each byte up; past the end, zeros.</summary>
		struct BitReader
		{
			readonly byte[] data;
			readonly int end;
			int position;

			public BitReader(byte[] data, int start, int end)
			{
				this.data = data;
				this.end = end * 8;
				position = start * 8;
			}

			public int ReadBit()
			{
				if (position >= end)
					return 0;

				var bit = (data[position >> 3] >> (position & 7)) & 1;
				position++;
				return bit;
			}

			public int ReadBits(int count)
			{
				var value = 0;
				for (var i = 0; i < count; i++)
					value |= ReadBit() << i;

				return value;
			}
		}

		/// <summary>
		/// A Huffman tree flattened in depth-first order: a node holds Node and the size of its 0 subtree, which
		/// follows it, and the 1 subtree after that. Video trees keep their last three values in escape leaves.
		/// </summary>
		sealed class Tree
		{
			readonly int[] table;
			readonly int[] recent;

			Tree(int[] table, int[] recent)
			{
				this.table = table;
				this.recent = recent;
			}

			public static Tree ReadByteTree(ref BitReader bits)
			{
				var table = new List<int>();
				ReadByteNode(ref bits, table, 0);
				return new Tree(table.ToArray(), null);
			}

			static void ReadByteNode(ref BitReader bits, List<int> table, int depth)
			{
				if (bits.ReadBit() == 0 || depth > 32)
				{
					table.Add(bits.ReadBits(8));
					return;
				}

				var at = table.Count;
				table.Add(Node);
				ReadByteNode(ref bits, table, depth + 1);
				table[at] = Node | (table.Count - at - 1);
				ReadByteNode(ref bits, table, depth + 1);
			}

			public static Tree ReadVideoTree(ref BitReader bits)
			{
				// No tree: every value is 0.
				if (bits.ReadBit() == 0)
					return new Tree([0, 0], [1, 1, 1]);

				var low = bits.ReadBit() != 0 ? ReadByteTreeAndEnd(ref bits) : null;
				var high = bits.ReadBit() != 0 ? ReadByteTreeAndEnd(ref bits) : null;
				int[] escapes = [bits.ReadBits(16), bits.ReadBits(16), bits.ReadBits(16)];
				int[] recent = [-1, -1, -1];

				var table = new List<int>();
				ReadVideoNode(ref bits, table, low, high, escapes, recent, 0);
				bits.ReadBit();

				// Escapes absent from the tree still take part in the shuffle, out of reach.
				for (var i = 0; i < 3; i++)
				{
					if (recent[i] < 0)
					{
						recent[i] = table.Count;
						table.Add(0);
					}
				}

				return new Tree(table.ToArray(), recent);
			}

			static Tree ReadByteTreeAndEnd(ref BitReader bits)
			{
				var tree = ReadByteTree(ref bits);
				bits.ReadBit();
				return tree;
			}

			static void ReadVideoNode(ref BitReader bits, List<int> table, Tree low, Tree high, int[] escapes, int[] recent, int depth)
			{
				if (bits.ReadBit() == 0 || depth > 64)
				{
					var value = (low?.Decode(ref bits) ?? 0) | ((high?.Decode(ref bits) ?? 0) << 8);
					for (var i = 0; i < 3; i++)
					{
						if (value == escapes[i])
						{
							recent[i] = table.Count;
							value = 0;
							break;
						}
					}

					table.Add(value);
					return;
				}

				var at = table.Count;
				table.Add(Node);
				ReadVideoNode(ref bits, table, low, high, escapes, recent, depth + 1);
				table[at] = Node | (table.Count - at - 1);
				ReadVideoNode(ref bits, table, low, high, escapes, recent, depth + 1);
			}

			public void ResetRecent()
			{
				foreach (var r in recent)
					table[r] = 0;
			}

			public int Decode(ref BitReader bits)
			{
				var i = 0;
				while ((table[i] & Node) != 0)
				{
					if (bits.ReadBit() != 0)
						i += table[i] & (Node - 1);

					i++;
				}

				var value = table[i];
				if (recent != null && value != table[recent[0]])
				{
					table[recent[2]] = table[recent[1]];
					table[recent[1]] = table[recent[0]];
					table[recent[0]] = value;
				}

				return value;
			}
		}
	}
}
