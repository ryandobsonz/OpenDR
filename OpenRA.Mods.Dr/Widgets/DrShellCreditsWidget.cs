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
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>
	/// The original credits, rolling up through their box as the original's shell rolls them: Activision's
	/// (USACREDT.TXT, which the 1.8.2 patch heads with its own) and Auran's (AUSCREDT.TXT) side by side,
	/// then the rest (CREDITS.TXT) across the box; the expansion's (ADDCREDT.TXT) are left out, as the
	/// original leaves them out of its own campaign. A line "~T" is a title, "~N" a name, any other a note. They
	/// start below the box after a pause, move Speed pixels a tick, and come round again once past.
	/// </summary>
	public class DrShellCreditsWidget : DrShellAreaWidget
	{
		public readonly string TitleFont = "font14r";
		public readonly string NameFont = "font14";
		public readonly string NoteFont = "font12n";

		[Desc("Where the two columns are centred, in the 640x480 screen.")]
		public readonly int LeftCentre = 220;
		public readonly int RightCentre = 420;

		[Desc("Lines added after the original's.")]
		public readonly string[] Extra = [];

		public readonly int StartOffset = 350;
		public readonly int Pause = 75;
		public readonly int MaxSpeed = 30;

		public int Speed = 1;

		readonly List<(string Text, string Font, float Centre, int Y)> lines = new();
		int height;
		int offset;
		int pause;

		public void Restart()
		{
			Speed = 1;
			offset = StartOffset;
			pause = Pause;
		}

		public void Faster() => Speed = Math.Min(Speed + 1, MaxSpeed);
		public void Slower() => Speed = Math.Max(Speed - 1, -MaxSpeed);

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			Restart();
		}

		void Layout()
		{
			var left = Read("USACREDT.TXT");
			var right = Read("AUSCREDT.TXT");
			var leftY = Place(left, LeftCentre, 0);
			var rightY = Place(right, RightCentre, 0);
			var y = Place(Read("CREDITS.TXT"), Area.X + Area.Width / 2f, Math.Max(leftY, rightY));
			height = Place(Extra, Area.X + Area.Width / 2f, y + Shell.Art.GetFont(NameFont).Height);
		}

		static string[] Read(string file)
		{
			if (!Game.ModData.DefaultFileSystem.TryOpen($"content|shell/{file}", out var stream))
				return [];

			using (var reader = new StreamReader(stream, Encoding.Latin1))
				return reader.ReadToEnd().Replace("\r", "").Split('\n');
		}

		int Place(IEnumerable<string> source, float centre, int y)
		{
			foreach (var raw in source)
			{
				var line = raw.Trim();
				var font = line.StartsWith("~T", StringComparison.Ordinal) ? TitleFont : line.StartsWith("~N", StringComparison.Ordinal) ? NameFont : NoteFont;
				if (line.StartsWith('~'))
					line = line[2..].Trim();

				if (line.Length > 0)
					lines.Add((line, font, centre, y));

				y += Shell.Art.GetFont(font).Height;
			}

			return y;
		}

		public override void Tick()
		{
			if (lines.Count == 0 && height == 0)
				return;

			if (pause > 0)
			{
				pause--;
				return;
			}

			// Round again once the last line has gone over the top, or, rolling back, the first under the bottom.
			offset -= Speed;
			if (offset < -height)
				offset = StartOffset;
			else if (offset > StartOffset)
				offset = 1 - height;
		}

		public override void Draw()
		{
			if (lines.Count == 0 && height == 0)
				Layout();

			Game.Renderer.EnableScissor(RenderBounds);
			foreach (var (text, fontName, centre, y) in lines)
			{
				var top = Area.Y + offset + y;
				var font = Shell.Art.GetFont(fontName);
				if (top + font.Height < Area.Y || top > Area.Bottom)
					continue;

				Shell.DrawText(font, text, new float2(centre - font.Measure(text) / 2f, top));
			}

			Game.Renderer.DisableScissor();
		}
	}
}
