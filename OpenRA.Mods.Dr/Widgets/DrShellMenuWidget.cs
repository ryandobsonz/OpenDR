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
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Dr.Graphics;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>
	/// A list of choices, a line each in the original shell's button fonts, lit under the mouse; the original's
	/// list boxes also show the chosen line in its selected font. Longer lists scroll, by the wheel or Scroll.
	/// </summary>
	public class DrShellMenuWidget : DrShellAreaWidget
	{
		public readonly string Font = "font14n";
		public readonly string HoverFont = "font14o";
		public readonly string SelectedFont = "font14s";

		[Desc("Pixels from one line to the next.")]
		public readonly int LineHeight = 25;

		public Func<IReadOnlyList<string>> GetItems = () => [];
		public Func<int> GetSelected = () => -1;
		public Action<int> OnSelect = _ => { };

		int hover = -1;
		int scroll;

		int VisibleLines => Area.Height / LineHeight;

		public bool CanScrollUp => scroll > 0;
		public bool CanScrollDown => scroll + VisibleLines < GetItems().Count;

		public void Scroll(int lines) => scroll = Math.Max(0, Math.Min(scroll + lines, GetItems().Count - VisibleLines));

		public void ScrollToTop() => scroll = 0;

		int ItemAt(int2 screen)
		{
			var p = Shell.ToShell(screen);
			var line = (int)Math.Floor((p.Y - Area.Y) / LineHeight);
			var i = scroll + line;
			return p.X >= Area.X && p.X < Area.Right && line >= 0 && line < VisibleLines && i < GetItems().Count ? i : -1;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (Blocked)
				return false;

			if (mi.Event == MouseInputEvent.Move)
			{
				hover = ItemAt(mi.Location);
				return false;
			}

			if (mi.Event == MouseInputEvent.Scroll && ItemAt(mi.Location) >= 0)
			{
				Scroll(-Math.Sign(mi.Delta.Y));
				hover = ItemAt(mi.Location);
				return true;
			}

			if (mi.Button != MouseButton.Left || mi.Event != MouseInputEvent.Down)
				return false;

			var item = ItemAt(mi.Location);
			if (item < 0)
				return false;

			Game.Sound.PlayNotification(Game.ModData.DefaultRules, null, "Sounds", ChromeMetrics.Get<string>("ClickSound"), null);
			OnSelect(item);
			hover = -1;
			return true;
		}

		public override void MouseExited()
		{
			hover = -1;
		}

		public override void Draw()
		{
			var items = GetItems();
			scroll = Math.Max(0, Math.Min(scroll, items.Count - VisibleLines));
			var selected = GetSelected();
			var normal = Shell.Art.GetFont(Font);
			var lit = Shell.Art.GetFont(HoverFont);
			var chosen = Shell.Art.GetFont(SelectedFont);
			for (var line = 0; line < VisibleLines && scroll + line < items.Count; line++)
			{
				var i = scroll + line;
				var font = i == selected ? chosen : i == hover ? lit : normal;
				Shell.DrawText(font, Fit(font, items[i]), new float2(Area.X, Area.Y + line * LineHeight + (LineHeight - font.Height) / 2f));
			}
		}

		/// <summary>The text, cut short with an ellipsis where it would run past the list's width.</summary>
		string Fit(DrShellFont font, string text)
		{
			if (font.Measure(text) <= Area.Width)
				return text;

			while (text.Length > 0 && font.Measure(text + "...") > Area.Width)
				text = text[..^1];

			return text + "...";
		}
	}
}
