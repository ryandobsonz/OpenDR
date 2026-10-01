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
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA.Mods.Dr.FileFormats
{
	/// <summary>An image from the original shell: 8-bit pixels and their own palette.</summary>
	public sealed class DrShellImage
	{
		public readonly int Width;
		public readonly int Height;
		public readonly byte[] Pixels;
		public readonly Color[] Palette;

		public DrShellImage(int width, int height, byte[] pixels, Color[] palette)
		{
			Width = width;
			Height = height;
			Pixels = pixels;
			Palette = palette;
		}
	}

	/// <summary>
	/// The original game's menu art, dark/shell/shell.rld with its index shell.rli: screens, overlays and
	/// bitmap fonts. The index ("ILR.") lists named entries; each is LZ packed (a flag byte per eight items,
	/// set for a literal byte, clear for a 16-bit reference: 12 bits of distance back, 4 of length - 3).
	/// Unpacked, an entry is a "TLF." container of chunks: "3BGR" a 256-colour palette and "LXIP" the pixels.
	/// </summary>
	public sealed class DrShellLibrary
	{
		readonly struct Entry(int offset, int packedSize, int size)
		{
			public readonly int Offset = offset;
			public readonly int PackedSize = packedSize;
			public readonly int Size = size;
		}

		readonly Dictionary<string, Entry> index = new(StringComparer.OrdinalIgnoreCase);
		readonly byte[] data;

		public DrShellLibrary(Stream rli, Stream rld)
		{
			var header = rli.ReadBytes(12);
			if (Encoding.ASCII.GetString(header, 0, 4) != "ILR.")
				throw new InvalidDataException("Not a shell library index");

			var count = BitConverter.ToInt32(header, 8);
			for (var i = 0; i < count; i++)
			{
				var e = rli.ReadBytes(32);
				var name = Encoding.ASCII.GetString(e, 0, 12).TrimEnd('\0');
				index[name] = new Entry(BitConverter.ToInt32(e, 20), BitConverter.ToInt32(e, 24), BitConverter.ToInt32(e, 28));
			}

			data = rld.ReadAllBytes();
		}

		/// <summary>The library in the installed content, or null when the import has not copied it.</summary>
		public static DrShellLibrary Load(IReadOnlyFileSystem fileSystem)
		{
			if (!fileSystem.TryOpen("content|shell/shell.rli", out var rli))
				return null;

			using (rli)
			{
				if (!fileSystem.TryOpen("content|shell/shell.rld", out var rld))
					return null;

				using (rld)
					return new DrShellLibrary(rli, rld);
			}
		}

		public bool Contains(string name) => index.ContainsKey(name);

		public DrShellImage GetImage(string name)
		{
			if (!index.TryGetValue(name, out var entry))
				throw new KeyNotFoundException($"The shell library has no image {name}");

			var tlf = Unpack(entry);
			if (Encoding.ASCII.GetString(tlf, 0, 4) != "TLF.")
				throw new InvalidDataException($"Shell image {name} is not a TLF container");

			Color[] palette = null;
			for (var p = 8; p + 8 <= tlf.Length;)
			{
				var tag = Encoding.ASCII.GetString(tlf, p, 4);
				var size = BitConverter.ToInt32(tlf, p + 4);
				if (size <= 0)
					break;

				if (tag == "3BGR")
				{
					palette = new Color[256];
					for (var i = 0; i < 256; i++)
						palette[i] = Color.FromArgb(i == 0 ? 0 : 255, tlf[p + 8 + 3 * i], tlf[p + 9 + 3 * i], tlf[p + 10 + 3 * i]);
				}
				else if (tag == "LXIP" && palette != null)
				{
					var width = BitConverter.ToUInt16(tlf, p + 12);
					var height = BitConverter.ToUInt16(tlf, p + 14);
					var pixels = new byte[width * height];
					Array.Copy(tlf, p + 16, pixels, 0, pixels.Length);
					return new DrShellImage(width, height, pixels, palette);
				}

				p += size;
			}

			throw new InvalidDataException($"Shell image {name} has no palette or pixels");
		}

		byte[] Unpack(Entry entry)
		{
			var output = new byte[entry.Size];
			var o = 0;
			var i = entry.Offset;
			var end = entry.Offset + entry.PackedSize;
			while (o < output.Length && i < end)
			{
				var flags = data[i++];
				for (var bit = 0; bit < 8 && o < output.Length && i < end; bit++)
				{
					if ((flags & (1 << bit)) != 0)
					{
						output[o++] = data[i++];
						continue;
					}

					var word = data[i] | (data[i + 1] << 8);
					i += 2;
					var distance = 0x1000 - (word & 0xFFF);
					var length = (word >> 12) + 3;
					for (var k = 0; k < length && o < output.Length; k++, o++)
						output[o] = o >= distance ? output[o - distance] : (byte)0;
				}
			}

			return output;
		}
	}
}
