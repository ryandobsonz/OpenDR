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
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>A list of choices, a line each in the original shell's button fonts, lit under the mouse.</summary>
	public class DrShellMenuWidget : DrShellAreaWidget
	{
		public readonly string Font = "font14n";
		public readonly string HoverFont = "font14o";

		[Desc("Pixels from one line to the next.")]
		public readonly int LineHeight = 25;

		public Func<IReadOnlyList<string>> GetItems = () => [];
		public Action<int> OnSelect = _ => { };

		int hover = -1;

		int ItemAt(int2 screen)
		{
			var p = Shell.ToShell(screen);
			var i = (int)Math.Floor((p.Y - Area.Y) / LineHeight);
			return p.X >= Area.X && p.X < Area.Right && i >= 0 && i < GetItems().Count && (i + 1) * LineHeight <= Area.Height ? i : -1;
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
			var normal = Shell.Art.GetFont(Font);
			var lit = Shell.Art.GetFont(HoverFont);
			for (var i = 0; i < items.Count && (i + 1) * LineHeight <= Area.Height; i++)
			{
				var font = i == hover ? lit : normal;
				Shell.DrawText(font, items[i], new float2(Area.X, Area.Y + i * LineHeight + (LineHeight - font.Height) / 2f));
			}
		}
	}
}
