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
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>
	/// A button of the original shell: text in its bitmap fonts (normal, under the mouse, selected), and an
	/// image or image frame that lights up under the mouse, such as the cube's arrows.
	/// </summary>
	public class DrShellButtonWidget : DrShellAreaWidget
	{
		[FluentReference]
		public readonly string Text = null;

		public readonly string Font = "font12n";
		public readonly string HoverFont = "font12o";
		public readonly string SelectedFont = "font12s";
		public readonly TextAlign Align = TextAlign.Center;

		[Desc("An image drawn at the area's top left (plus ImageOffset): NormalFrame normally, HoverFrame under the mouse or selected.")]
		public readonly string Image = null;
		public readonly int Frames = 1;
		public readonly bool Horizontal = false;
		public readonly int NormalFrame = -1;
		public readonly int HoverFrame = 0;
		public readonly int2 ImageOffset = int2.Zero;

		public Func<string> GetText;
		public Func<bool> IsDisabled = () => false;
		public Func<bool> IsHighlighted = () => false;
		public Action OnClick = () => { };

		bool depressed;

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			var text = Text != null ? FluentProvider.GetMessage(Text) : null;
			GetText ??= () => text;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Button != MouseButton.Left || (Blocked && !HasMouseFocus))
				return false;

			if (mi.Event == MouseInputEvent.Down)
			{
				if (IsDisabled() || !TakeMouseFocus(mi))
					return false;

				depressed = true;
				Game.Sound.PlayNotification(Game.ModData.DefaultRules, null, "Sounds", ChromeMetrics.Get<string>("ClickSound"), null);
				return true;
			}

			if (mi.Event == MouseInputEvent.Up && HasMouseFocus)
			{
				var click = depressed && !IsDisabled() && EventBounds.Contains(mi.Location);
				depressed = false;
				YieldMouseFocus(mi);
				if (click)
					OnClick();

				return true;
			}

			return false;
		}

		public override void Draw()
		{
			var disabled = IsDisabled();
			var hover = !disabled && !Blocked && (Ui.MouseOverWidget == this || depressed);
			var selected = IsHighlighted();

			if (Image != null)
			{
				var frame = hover || selected ? HoverFrame : NormalFrame;
				if (frame >= 0)
					Shell.DrawSprite(Shell.Art.GetFrames(Image, Frames, Horizontal)[frame], new float2(Area.X + ImageOffset.X, Area.Y + ImageOffset.Y));
			}

			var text = GetText();
			if (string.IsNullOrEmpty(text))
				return;

			var font = Shell.Art.GetFont(selected && SelectedFont != null ? SelectedFont : hover && HoverFont != null ? HoverFont : Font);
			DrShellLabelWidget.DrawAligned(Shell, font, text, Area, Align, disabled ? 0.4f : 1f);
		}
	}
}
