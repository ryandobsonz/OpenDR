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

using System.Collections.Generic;
using System.IO;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Dr.SpriteLoaders
{
	/// <summary>
	/// The original's cursors (graphics/INTFACE/MOUSE.CRS, read by Cursor.c in dkreign.exe): "CRSR", version
	/// 0x200, the number of 32x32 frames and the frames, then the cursors: their number, and for each its
	/// hotspot, its number of frames and the frames in the order they play, repeats included. The frames come
	/// out cursor after cursor in that order, so cursor.yaml's sequences are the file's own cursors.
	/// </summary>
	public class DrCrsLoader : ISpriteLoader
	{
		const int Width = 32;

		sealed class DrCrsFrame : ISpriteFrame
		{
			public SpriteFrameType Type => SpriteFrameType.Indexed8;
			public Size Size => new(Width, Width);
			public Size FrameSize => Size;
			public float2 Offset => float2.Zero;
			public byte[] Data { get; }
			public bool DisableExportPadding => false;

			public DrCrsFrame(byte[] data)
			{
				Data = data;
			}
		}

		public bool TryParseSprite(Stream s, string filename, out ISpriteFrame[] frames, out TypeDictionary metadata)
		{
			metadata = null;
			frames = null;
			var start = s.Position;
			if (s.Length - start < 12 || s.ReadASCII(4) != "CRSR" || s.ReadInt32() != 0x200)
			{
				s.Position = start;
				return false;
			}

			var count = s.ReadInt32();
			var images = new DrCrsFrame[count];
			for (var i = 0; i < count; i++)
				images[i] = new DrCrsFrame(s.ReadBytes(Width * Width));

			// The demo's file has no cursor table: its frames as they are.
			if (s.Position + 4 > s.Length)
			{
				frames = images;
				return true;
			}

			var played = new List<ISpriteFrame>();
			var cursors = s.ReadInt32();
			for (var c = 0; c < cursors; c++)
			{
				s.ReadInt32();
				s.ReadInt32();
				var length = s.ReadInt32();
				for (var f = 0; f < length; f++)
					played.Add(images[s.ReadInt32()]);
			}

			frames = played.ToArray();
			return true;
		}
	}
}
