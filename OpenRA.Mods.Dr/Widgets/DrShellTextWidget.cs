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
using System.Text.RegularExpressions;
using OpenRA.Mods.Dr.Graphics;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>
	/// Briefing text in the original shell's font, wrapped to its area and scrolled by the mouse wheel. It
	/// reads the briefing files' markup: \n breaks a line, \c centres it, \s is a space; line ends are spaces.
	/// </summary>
	public class DrShellTextWidget : DrShellAreaWidget
	{
		public readonly string Font = "font12";

		[Desc("Extra pixels between lines.")]
		public readonly int LineSpacing = 2;

		readonly List<(string Text, bool Centered)> lines = new();
		string source;
		int scroll;

		DrShellFont font;

		DrShellFont TextFont => font ??= Shell.Art.GetFont(Font);
		int LineHeight => TextFont.Height + LineSpacing;
		int VisibleLines => Math.Max(1, Area.Height / LineHeight);

		public bool CanScrollUp => scroll > 0;
		public bool CanScrollDown => scroll + VisibleLines < lines.Count;

		public void SetText(string markup)
		{
			source = markup ?? "";
			scroll = 0;
			Layout();
		}

		public void Scroll(int by)
		{
			scroll = Math.Clamp(scroll + by, 0, Math.Max(0, lines.Count - VisibleLines));
		}

		void Layout()
		{
			lines.Clear();
			var text = Regex.Replace(source, @"\s*\r?\n\s*", " ").Trim();
			foreach (var paragraph in text.Split("\\n"))
			{
				var centered = paragraph.Contains("\\c");
				var p = paragraph.Replace("\\c", "").TrimEnd();
				p = p.StartsWith("\\s", StringComparison.Ordinal) ? p.Replace("\\s", " ") : p.Replace("\\s", " ").TrimStart();
				Wrap(p, centered);
			}

			while (lines.Count > 0 && lines[^1].Text.Length == 0)
				lines.RemoveAt(lines.Count - 1);
		}

		void Wrap(string paragraph, bool centered)
		{
			if (paragraph.Trim().Length == 0)
			{
				lines.Add(("", false));
				return;
			}

			var indent = paragraph.Length - paragraph.TrimStart().Length;
			var line = paragraph[..indent];
			foreach (var word in paragraph.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				var candidate = line.Trim().Length == 0 ? line + word : line + " " + word;
				if (TextFont.Measure(candidate) > Area.Width && line.Trim().Length > 0)
				{
					lines.Add((line, centered));
					line = word;
				}
				else
					line = candidate;
			}

			lines.Add((line, centered));
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Event != MouseInputEvent.Scroll)
				return false;

			Scroll(-Math.Sign(mi.Delta.Y) * 3);
			return true;
		}

		public override void Draw()
		{
			if (source == null)
				return;

			Game.Renderer.EnableScissor(RenderBounds);
			var y = (float)Area.Y;
			for (var i = scroll; i < lines.Count && i < scroll + VisibleLines; i++)
			{
				var (text, centered) = lines[i];
				var x = centered ? Area.X + (Area.Width - TextFont.Measure(text)) / 2f : Area.X;
				Shell.DrawText(TextFont, text, new float2(x, y));
				y += LineHeight;
			}

			Game.Renderer.DisableScissor();
		}
	}
}
