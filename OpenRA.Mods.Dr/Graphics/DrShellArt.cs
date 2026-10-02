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
using OpenRA.Graphics;
using OpenRA.Mods.Dr.FileFormats;
using OpenRA.Primitives;

namespace OpenRA.Mods.Dr.Graphics
{
	/// <summary>
	/// The original art as sprites: the shell's, or the in-game interface's. Each image is enlarged by a whole factor with nearest
	/// neighbour, then drawn at the screen's scale with linear filtering: sharp pixels without the
	/// uneven columns that a plain nearest neighbour stretch to a fractional scale leaves.
	/// </summary>
	public sealed class DrShellArt : IDisposable
	{
		readonly IDrImageSource library;
		readonly Dictionary<string, (Sheet Sheet, Sprite[] Frames)> images = new(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, DrShellFont> fonts = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>The whole factor the art is enlarged by before filtering, at most 3: a screen then fits a 2048 texture.</summary>
		public readonly int Upscale;

		public DrShellArt(IDrImageSource library, int upscale)
		{
			this.library = library;
			Upscale = Math.Clamp(upscale, 1, 3);
		}

		public bool Contains(string name) => library.Contains(name);

		/// <summary>An image cut into equal frames, stacked vertically unless horizontal. Colour 0 is transparent unless opaque.</summary>
		public Sprite[] GetFrames(string name, int count = 1, bool horizontal = false, bool opaque = false)
		{
			var key = $"{name}:{count}:{horizontal}:{opaque}";
			if (images.TryGetValue(key, out var cached))
				return cached.Frames;

			var image = library.GetImage(name);
			var k = Upscale;
			var sheet = NewSheet(image.Width * k, image.Height * k);
			var data = sheet.GetData();
			Blit(image, new Rectangle(0, 0, image.Width, image.Height), data, sheet.Size.Width, 0, 0, k, opaque);
			var frames = new Sprite[count];
			var fw = horizontal ? image.Width / count : image.Width;
			var fh = horizontal ? image.Height : image.Height / count;
			for (var i = 0; i < count; i++)
			{
				var r = horizontal ? new Rectangle(i * fw * k, 0, fw * k, fh * k) : new Rectangle(0, i * fh * k, fw * k, fh * k);
				frames[i] = new Sprite(sheet, r, TextureChannel.RGBA, 1f / k);
			}

			Commit(sheet);
			images[key] = (sheet, frames);
			return frames;
		}

		public Sprite Get(string name) => GetFrames(name)[0];

		readonly Dictionary<(string, Rectangle), Sprite> regions = [];

		/// <summary>A rectangle of an image, in its own pixels: the interface cuts frames of several sizes from one strip.</summary>
		public Sprite GetRegion(string name, Rectangle r)
		{
			if (regions.TryGetValue((name, r), out var sprite))
				return sprite;

			var image = Get(name);
			var k = Upscale;
			sprite = new Sprite(image.Sheet, new Rectangle(image.Bounds.X + r.X * k, image.Bounds.Y + r.Y * k, r.Width * k, r.Height * k), TextureChannel.RGBA, 1f / k);
			regions[(name, r)] = sprite;
			return sprite;
		}

		public DrShellFont GetFont(string name)
		{
			if (!fonts.TryGetValue(name, out var font))
				fonts[name] = font = new DrShellFont(library.GetImage(name), Upscale);

			return font;
		}

		/// <summary>Frees the full-screen images other than those named; they are large at a high upscale.</summary>
		public void ReleaseExcept(ICollection<string> keep)
		{
			foreach (var key in new List<string>(images.Keys))
			{
				var name = key[..key.IndexOf(':')];
				if (keep.Contains(name) || images[key].Frames[0].Bounds.Width * images[key].Frames[0].Bounds.Height < 640 * 480 * Upscale * Upscale)
					continue;

				images[key].Sheet.Dispose();
				images.Remove(key);
				foreach (var region in new List<(string, Rectangle)>(regions.Keys))
					if (region.Item1 == name)
						regions.Remove(region);
			}
		}

		/// <summary>Textures are powers of two in size; the art takes the top left.</summary>
		internal static Sheet NewSheet(int width, int height)
		{
			return new Sheet(SheetType.BGRA, new Size(Exts.NextPowerOf2(width), Exts.NextPowerOf2(height)));
		}

		internal static void Blit(DrShellImage image, Rectangle source, byte[] data, int stride, int x0, int y0, int k, bool opaque = false)
		{
			for (var y = 0; y < source.Height; y++)
			{
				for (var x = 0; x < source.Width; x++)
				{
					var c = image.Palette[image.Pixels[(source.Y + y) * image.Width + source.X + x]];
					if (c.A == 0 && !opaque)
						continue;

					for (var dy = 0; dy < k; dy++)
					{
						var o = 4 * ((y0 + y * k + dy) * stride + x0 + x * k);
						for (var dx = 0; dx < k; dx++, o += 4)
						{
							data[o] = c.B;
							data[o + 1] = c.G;
							data[o + 2] = c.R;
							data[o + 3] = 255;
						}
					}
				}
			}
		}

		/// <summary>Draws a sprite into a screen rectangle, faded by alpha.</summary>
		public static void DrawQuad(Sprite sprite, float2 topLeft, float2 size, float alpha = 1f)
		{
			var a = new float3(topLeft, 0);
			var b = new float3(topLeft.X + size.X, topLeft.Y, 0);
			var c = new float3(topLeft + size, 0);
			var d = new float3(topLeft.X, topLeft.Y + size.Y, 0);

			// The renderer blends premultiplied colours, so the colour fades with the alpha.
			Game.Renderer.RgbaSpriteRenderer.DrawSprite(sprite, a, b, c, d, new float3(alpha, alpha, alpha), alpha);
		}

		internal static void Commit(Sheet sheet)
		{
			sheet.CommitBufferedData();
			sheet.GetTexture().ScaleFilter = TextureScaleFilter.Linear;
			sheet.ReleaseBuffer();
		}

		public void Dispose()
		{
			foreach (var image in images.Values)
				image.Sheet.Dispose();

			foreach (var font in fonts.Values)
				font.Dispose();

			images.Clear();
			fonts.Clear();
			regions.Clear();
		}
	}

	/// <summary>
	/// A bitmap font from the shell: a strip of glyphs, one per character code, divided by full-height
	/// columns of the colour in its top left pixel. Colour 0 is transparent.
	/// </summary>
	public sealed class DrShellFont : IDisposable
	{
		const int AtlasWidth = 512;
		const int Padding = 2;

		readonly Sheet sheet;
		readonly Sprite[] glyphs;
		readonly int[] advance;

		public readonly int Height;

		public DrShellFont(DrShellImage image, int k)
		{
			Height = image.Height;
			var marker = image.Pixels[0];
			var dividers = new List<int>();
			for (var x = 0; x < image.Width; x++)
			{
				var full = true;
				for (var y = 0; y < image.Height && full; y++)
					full = image.Pixels[y * image.Width + x] == marker;

				if (full)
					dividers.Add(x);
			}

			// Trim each glyph to its ink: the cells carry uneven blank columns, and the space's cell is far wider
			// than a space. Glyphs are then set a pixel apart, and a space is about two thirds of a letter.
			var cells = new List<Rectangle>();
			for (var i = 0; i + 1 < dividers.Count; i++)
			{
				int left = dividers[i + 1], right = dividers[i];
				for (var x = dividers[i] + 1; x < dividers[i + 1]; x++)
				{
					for (var y = 0; y < image.Height; y++)
					{
						if (image.Pixels[y * image.Width + x] != 0)
						{
							left = Math.Min(left, x);
							right = Math.Max(right, x);
							break;
						}
					}
				}

				cells.Add(right >= left ? new Rectangle(left, 0, right - left + 1, image.Height) : new Rectangle(dividers[i] + 1, 0, 0, image.Height));
			}

			var letters = 0;
			var letterWidth = 0;
			for (var c = 'A'; c <= 'z' && c < cells.Count; c++)
			{
				if (char.IsLetter(c) && cells[c].Width > 0)
				{
					letters++;
					letterWidth += cells[c].Width;
				}
			}

			var space = Math.Max(3, (int)Math.Round(letters > 0 ? 0.6 * letterWidth / letters : 3));

			// Pack the glyphs into rows, so the sheet stays within texture limits at any upscale.
			var positions = new int2[cells.Count];
			int px = Padding, py = Padding, rowHeight = image.Height + Padding;
			foreach (var (r, i) in Indexed(cells))
			{
				if (px + r.Width + Padding > AtlasWidth)
				{
					px = Padding;
					py += rowHeight;
				}

				positions[i] = new int2(px, py);
				px += r.Width + Padding;
			}

			sheet = DrShellArt.NewSheet(AtlasWidth * k, (py + rowHeight) * k);
			var data = sheet.GetData();
			glyphs = new Sprite[cells.Count];
			advance = new int[cells.Count];
			for (var i = 0; i < cells.Count; i++)
			{
				var r = cells[i];
				DrShellArt.Blit(image, r, data, sheet.Size.Width, positions[i].X * k, positions[i].Y * k, k);
				glyphs[i] = new Sprite(sheet, new Rectangle(positions[i].X * k, positions[i].Y * k, r.Width * k, r.Height * k), TextureChannel.RGBA, 1f / k);
				advance[i] = i == ' ' ? space + 1 : r.Width > 0 ? r.Width + 1 : 0;
			}

			DrShellArt.Commit(sheet);
		}

		static IEnumerable<(Rectangle, int)> Indexed(List<Rectangle> cells)
		{
			for (var i = 0; i < cells.Count; i++)
				yield return (cells[i], i);
		}

		int Glyph(char c) => c < glyphs.Length ? c : '?';

		public int Measure(string text)
		{
			var width = 0;
			foreach (var c in text)
				width += advance[Glyph(c)];

			return width;
		}

		/// <summary>Draws text with its top left at a point of the 640x480 shell, scaled to the screen.</summary>
		public void Draw(string text, float2 origin, float2 scale, float2 position, float alpha = 1f)
		{
			var x = position.X;
			foreach (var ch in text)
			{
				var g = Glyph(ch);
				if (glyphs[g].Bounds.Width > 0)
				{
					var location = origin + new float2(x * scale.X, position.Y * scale.Y);
					DrShellArt.DrawQuad(glyphs[g], location, new float2(glyphs[g].Size.X * scale.X, glyphs[g].Size.Y * scale.Y), alpha);
				}

				x += advance[g];
			}
		}

		public void Dispose()
		{
			sheet.Dispose();
		}
	}
}
