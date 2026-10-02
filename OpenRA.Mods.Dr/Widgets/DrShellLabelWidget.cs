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
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Dr.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>A line of text in one of the original shell's bitmap fonts, centred vertically in its area or at its top.</summary>
	public class DrShellLabelWidget : DrShellAreaWidget
	{
		[FluentReference]
		public readonly string Text = null;

		public readonly string Font = "font14";
		public readonly TextAlign Align = TextAlign.Center;

		[Desc("Draw from the area's top, as the original's plain text is, rather than centred in its height.")]
		public readonly bool Top = false;

		public Func<string> GetText;

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			var text = Text != null ? FluentProvider.GetMessage(Text) : null;
			GetText ??= () => text;
		}

		public override void Draw()
		{
			var text = GetText();
			if (!string.IsNullOrEmpty(text))
				DrawAligned(Shell, Shell.Art.GetFont(Font), text, Area, Align, top: Top);
		}

		/// <summary>Draws text, a line per '\n', aligned in an area of the shell and centred vertically in it, or from its top.</summary>
		public static void DrawAligned(DrShellWidget shell, DrShellFont font, string text, Rectangle area, TextAlign align, float alpha = 1f, bool top = false)
		{
			var lines = text.Split('\n');
			var y = top ? area.Y : area.Y + (area.Height - lines.Length * font.Height) / 2f;
			foreach (var line in lines)
			{
				var width = font.Measure(line);
				var x = align switch
				{
					TextAlign.Left => area.X,
					TextAlign.Right => area.Right - width,
					_ => area.X + (area.Width - width) / 2f
				};

				shell.DrawText(font, line, new float2(x, y), alpha);
				y += font.Height;
			}
		}
	}
}
