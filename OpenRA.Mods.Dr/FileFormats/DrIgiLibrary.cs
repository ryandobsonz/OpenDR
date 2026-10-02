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
using System.Text.RegularExpressions;
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA.Mods.Dr.FileFormats
{
	/// <summary>
	/// The original in-game interface's art ("IGI"), dark/graphics/INTFACE/IGI, which the import copies to
	/// content/igi: 8-bit Windows bitmaps and PCX fonts, each with its own palette; colour 0 is transparent.
	/// Its text comes from dark/local/MLSTRING.CFG, the game's own strings ("#define IGI_BUUPGR "Upgrade"").
	/// </summary>
	public sealed class DrIgiLibrary : IDrImageSource
	{
		static readonly Regex DefineRegex = new("^#define\\s+(\\w+)\\s+\"(.*)\"", RegexOptions.Compiled);

		readonly IReadOnlyFileSystem fileSystem;
		readonly Dictionary<string, DrShellImage> images = new(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, string> strings = new(StringComparer.Ordinal);

		DrIgiLibrary(IReadOnlyFileSystem fileSystem)
		{
			this.fileSystem = fileSystem;
			if (fileSystem.TryOpen("content|igi/MLSTRING.CFG", out var cfg))
			{
				using (cfg)
				using (var reader = new StreamReader(cfg))
				{
					string line;
					while ((line = reader.ReadLine()) != null)
					{
						var m = DefineRegex.Match(line);
						if (m.Success)
							strings[m.Groups[1].Value] = m.Groups[2].Value;
					}
				}
			}
		}

		/// <summary>The interface art in the installed content, or null when the import has not copied it.</summary>
		public static DrIgiLibrary Load(IReadOnlyFileSystem fileSystem)
		{
			return fileSystem.Exists("content|igi/TOPBTNS.BMP") ? new DrIgiLibrary(fileSystem) : null;
		}

		/// <summary>The game's string by its name, else the mod's own (Fluent, for what the remaster adds), else the name.</summary>
		public string GetString(string name, string fallback = null)
		{
			if (strings.TryGetValue(name, out var text))
				return text;

			return fallback ?? (FluentProvider.TryGetMessage(name, out var message) ? message : name);
		}

		public bool Contains(string name) => images.ContainsKey(name) || fileSystem.Exists("content|igi/" + name);

		public DrShellImage GetImage(string name)
		{
			if (images.TryGetValue(name, out var image))
				return image;

			if (!fileSystem.TryOpen("content|igi/" + name, out var stream))
				throw new FileNotFoundException($"The interface art has no {name}");

			using (stream)
			{
				var data = stream.ReadAllBytes();
				image = name.EndsWith(".pcx", StringComparison.OrdinalIgnoreCase) ? ReadPcx(data, name) : ReadBmp(data, name);
			}

			images[name] = image;
			return image;
		}

		/// <summary>An uncompressed 8-bit Windows bitmap: rows bottom up (unless the height is negative), padded to four bytes.</summary>
		static DrShellImage ReadBmp(byte[] d, string name)
		{
			if (d[0] != 'B' || d[1] != 'M')
				throw new InvalidDataException($"{name} is not a bitmap");

			var pixelOffset = BitConverter.ToInt32(d, 10);
			var headerSize = BitConverter.ToInt32(d, 14);
			var width = BitConverter.ToInt32(d, 18);
			var rawHeight = BitConverter.ToInt32(d, 22);
			var bpp = BitConverter.ToUInt16(d, 28);
			var compression = BitConverter.ToInt32(d, 30);
			var used = BitConverter.ToInt32(d, 46);
			if (bpp != 8 || compression != 0)
				throw new InvalidDataException($"{name} is not an uncompressed 8-bit bitmap");

			var palette = new Color[256];
			var count = used == 0 ? 256 : Math.Min(used, 256);
			for (var i = 0; i < count; i++)
			{
				var p = 14 + headerSize + 4 * i;
				palette[i] = Color.FromArgb(i == 0 ? 0 : 255, d[p + 2], d[p + 1], d[p]);
			}

			var height = Math.Abs(rawHeight);
			var stride = (width + 3) & ~3;
			var pixels = new byte[width * height];
			for (var y = 0; y < height; y++)
			{
				var row = rawHeight > 0 ? height - 1 - y : y;
				Array.Copy(d, pixelOffset + row * stride, pixels, y * width, width);
			}

			return new DrShellImage(width, height, pixels, palette);
		}

		/// <summary>An 8-bit PCX: run-length coded rows (a byte of 0xC0 and up repeats the next that many times, less 0xC0), the palette after a 12 at the end.</summary>
		static DrShellImage ReadPcx(byte[] d, string name)
		{
			if (d[0] != 0x0A || d[3] != 8 || d[65] != 1)
				throw new InvalidDataException($"{name} is not an 8-bit PCX");

			var width = BitConverter.ToUInt16(d, 8) - BitConverter.ToUInt16(d, 4) + 1;
			var height = BitConverter.ToUInt16(d, 10) - BitConverter.ToUInt16(d, 6) + 1;
			var bytesPerLine = BitConverter.ToUInt16(d, 66);

			var palette = new Color[256];
			var p = d.Length - 768;
			for (var i = 0; i < 256; i++)
				palette[i] = Color.FromArgb(i == 0 ? 0 : 255, d[p + 3 * i], d[p + 3 * i + 1], d[p + 3 * i + 2]);

			var pixels = new byte[width * height];
			var line = new byte[bytesPerLine];
			var o = 128;
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < bytesPerLine && o < p;)
				{
					var b = d[o++];
					var run = 1;
					if (b >= 0xC0)
					{
						run = b - 0xC0;
						b = d[o++];
					}

					for (; run > 0 && x < bytesPerLine; run--)
						line[x++] = b;
				}

				Array.Copy(line, 0, pixels, y * width, width);
			}

			return new DrShellImage(width, height, pixels, palette);
		}
	}
}
