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
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Dr.FileFormats;
using OpenRA.Mods.Dr.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>Where a control of the 640x480 interface goes on a wider screen.</summary>
	public enum DrIgiAnchor
	{
		/// <summary>Right of x 448 (the panel) to the screen's right edge, else to its left.</summary>
		Auto,
		Left,
		Right,

		/// <summary>Kept at its place from the middle of the map view: the credits, and the dialogs over the map.</summary>
		Center
	}

	/// <summary>
	/// The original game's in-game interface ("IGI", Igidisp.c and Igizone.c in dkreign.exe), from its own art
	/// in content/igi. The original is 640x480: a bar of buttons and the credits across the top of the map view
	/// (0-447, 32 high) and a panel down the right (448-639): two rows of tabs, the build menu or the tab's
	/// controls, the minimap and the resource bars. Here it is scaled by the screen's height over 480; the panel
	/// keeps to the right edge, the map view takes the rest of the width, and the top bar stretches across it.
	/// Its DrIgi* descendants are placed by their rectangles in the original screen.
	/// </summary>
	public class DrIgiWidget : Widget
	{
		public const int OriginalWidth = 640;
		public const int OriginalHeight = 480;
		public const int PanelLeft = 448;
		public const int TopBarHeight = 32;

		static readonly ConditionalWeakTable<ModData, DrIgiLibrary> Libraries = new();

		public DrShellArt Art { get; private set; }
		public DrIgiLibrary Library { get; private set; }
		public float Scale { get; private set; }
		public int2 Screen { get; private set; }

		/// <summary>The screen x of the panel's left edge, which is also the map view's right.</summary>
		public float PanelX => Screen.X - (OriginalWidth - PanelLeft) * Scale;

		/// <summary>The interface art in the installed content, or null if it has not been imported.</summary>
		public static DrIgiLibrary GetLibrary(ModData modData)
		{
			if (!Libraries.TryGetValue(modData, out var library))
			{
				library = DrIgiLibrary.Load(modData.DefaultFileSystem);
				if (library != null)
					Libraries.Add(modData, library);
			}

			return library;
		}

		readonly ModData modData;

		[ObjectCreator.UseCtor]
		public DrIgiWidget(ModData modData)
		{
			this.modData = modData;
		}

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			UpdateTransform();
			Library = GetLibrary(modData);
			if (Library != null)
				Art = new DrShellArt(Library, (int)Math.Ceiling(Scale * Game.Renderer.WindowScale - 0.01f));
		}

		void UpdateTransform()
		{
			var r = Game.Renderer.Resolution;
			Screen = new int2(r.Width, r.Height);
			Scale = r.Height / (float)OriginalHeight;
		}

		public float ScreenX(float x, DrIgiAnchor anchor)
		{
			if (anchor == DrIgiAnchor.Auto)
				anchor = x >= PanelLeft ? DrIgiAnchor.Right : DrIgiAnchor.Left;

			return anchor switch
			{
				DrIgiAnchor.Right => Screen.X - (OriginalWidth - x) * Scale,
				DrIgiAnchor.Center => PanelX / 2 + (x - PanelLeft / 2f) * Scale,
				_ => x * Scale,
			};
		}

		public Rectangle ToScreen(Rectangle area, DrIgiAnchor anchor)
		{
			var x = ScreenX(area.X, anchor);
			return Rectangle.FromLTRB((int)x, (int)(area.Y * Scale), (int)Math.Ceiling(x + area.Width * Scale), (int)Math.Ceiling(area.Bottom * Scale));
		}

		/// <summary>Draws a sprite of the art at a screen point, at the interface's scale.</summary>
		public void DrawSprite(Sprite sprite, float2 position, float alpha = 1f)
		{
			DrShellArt.DrawQuad(sprite, position, sprite.Size.XY * Scale, alpha);
		}

		public void DrawSprite(Sprite sprite, float2 position, float2 size, float alpha = 1f)
		{
			DrShellArt.DrawQuad(sprite, position, size, alpha);
		}

		public DrShellFont Font(string name) => Art.GetFont(name);

		public void DrawText(DrShellFont font, string text, float2 position, float alpha = 1f)
		{
			font.Draw(text, position, new float2(Scale, Scale), float2.Zero, alpha);
		}

		/// <summary>Draws text centred in a screen rectangle.</summary>
		public void DrawTextCentered(DrShellFont font, string text, Rectangle r, float alpha = 1f)
		{
			var size = new float2(font.Measure(text), font.Height) * Scale;
			DrawText(font, text, new float2((int)(r.X + (r.Width - size.X) / 2), (int)(r.Y + (r.Height - size.Y) / 2)), alpha);
		}

		public override void Tick()
		{
			UpdateTransform();
		}

		public override void Draw()
		{
			UpdateTransform();
			if (Art == null)
				return;

			// The original drew its interface over black, which shows through the art's gaps (the credits display).
			var panel = (int)PanelX;
			WidgetUtils.FillRectWithColor(new Rectangle(0, 0, panel, (int)Math.Ceiling(TopBarHeight * Scale)), Color.Black);
			WidgetUtils.FillRectWithColor(new Rectangle(panel, 0, Screen.X - panel, Screen.Y), Color.Black);
		}

		public override void Removed()
		{
			base.Removed();
			Art?.Dispose();
			Art = null;
		}

		// The tooltip strip (PT.BMP): text that appears after the original's InfoDelay (TACTICS.CFG, 800 ms).
		const int TooltipDelay = 800;
		readonly Stopwatch tooltipTimer = new();
		Func<string> tooltip;
		Widget tooltipOwner;

		public void SetTooltip(Widget owner, Func<string> text)
		{
			if (tooltipOwner == owner)
				return;

			tooltipOwner = owner;
			tooltip = text;
			tooltipTimer.Restart();
		}

		public void ClearTooltip(Widget owner)
		{
			if (tooltipOwner == owner)
			{
				tooltipOwner = null;
				tooltip = null;
			}
		}

		public override void DrawOuter()
		{
			base.DrawOuter();
			if (Art == null || tooltip == null || tooltipTimer.ElapsedMilliseconds < TooltipDelay || !tooltipOwner.IsVisible())
				return;

			var text = tooltip();
			if (string.IsNullOrEmpty(text))
				return;

			// The strip is 171x13; longer text widens it from the middle.
			var font = Font("FONT12W.PCX");
			var strip = Art.Get("PT.BMP");
			var width = Math.Max(strip.Size.X, font.Measure(text) + 10) * Scale;
			var height = strip.Size.Y * Scale;
			var owner = tooltipOwner.RenderBounds;
			var x = Math.Clamp(owner.X + (owner.Width - width) / 2, 0, Screen.X - width);
			var y = owner.Bottom + 2 * Scale;
			if (y + height > Screen.Y)
				y = owner.Y - height - 2 * Scale;

			var cap = 8;
			var size = strip.Size.XY;
			DrawSprite(Art.GetRegion("PT.BMP", new Rectangle(0, 0, cap, (int)size.Y)), new float2(x, y));
			DrawSprite(Art.GetRegion("PT.BMP", new Rectangle(cap, 0, (int)size.X - 2 * cap, (int)size.Y)), new float2(x + cap * Scale, y), new float2(width - 2 * cap * Scale, height));
			DrawSprite(Art.GetRegion("PT.BMP", new Rectangle((int)size.X - cap, 0, cap, (int)size.Y)), new float2(x + width - cap * Scale, y));
			DrawTextCentered(font, text, new Rectangle((int)x, (int)y, (int)width, (int)height));
		}
	}

	/// <summary>A widget placed by a rectangle of the 640x480 interface, with its pixel bounds kept to the screen.</summary>
	public abstract class DrIgiAreaWidget : Widget
	{
		[Desc("Left, top, width and height in the original 640x480 interface.")]
		public Rectangle Area = Rectangle.Empty;

		public DrIgiAnchor Anchor = DrIgiAnchor.Auto;

		[Desc("A tooltip from the game's strings (MLSTRING.CFG).")]
		public readonly string Tooltip = null;

		public Func<string> GetTooltip;

		DrIgiWidget igi;
		Rectangle placed;

		protected DrIgiWidget Igi
		{
			get
			{
				if (igi == null)
					for (var w = Parent; w != null && igi == null; w = w.Parent)
						igi = w as DrIgiWidget;

				return igi;
			}
		}

		protected DrShellArt Art => Igi.Art;
		protected float Scale => Igi.Scale;

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			if (Tooltip != null)
			{
				var text = Igi.Library?.GetString(Tooltip);
				GetTooltip ??= () => text;
			}

			Place();
		}

		/// <summary>Sets the pixel bounds from the area, so ordinary children (the radar) can fill them.</summary>
		void Place()
		{
			var r = Igi.ToScreen(Area, Anchor);
			if (r == placed)
				return;

			placed = r;
			var origin = Parent.RenderOrigin;
			Bounds = new WidgetBounds(r.X - origin.X, r.Y - origin.Y, r.Width, r.Height);
		}

		public override void Tick()
		{
			Place();
		}

		public override void MouseEntered()
		{
			if (GetTooltip != null)
				Igi.SetTooltip(this, GetTooltip);
		}

		public override void MouseExited()
		{
			Igi.ClearTooltip(this);
		}

		public override void Removed()
		{
			base.Removed();
			Igi?.ClearTooltip(this);
		}
	}

	/// <summary>An area holding ordinary widgets, such as the radar inside the minimap's frame.</summary>
	public class DrIgiAreaContainerWidget : DrIgiAreaWidget
	{
		public DrIgiAreaContainerWidget()
		{
			IgnoreMouseOver = true;
		}
	}

	/// <summary>A part of one of the interface's images, drawn at the area's top left.</summary>
	public class DrIgiImageWidget : DrIgiAreaWidget
	{
		public readonly string Image = null;

		[Desc("The part of the image, in its pixels; empty for all of it.")]
		public readonly Rectangle Source = Rectangle.Empty;

		[Desc("How far apart the frames are in the image: GetFrame picks one.")]
		public readonly int2 FrameStride = int2.Zero;

		public readonly bool ClickThrough = true;

		public Func<int> GetFrame = () => 0;

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			IgnoreMouseOver = ClickThrough;
		}

		public override bool HandleMouseInput(MouseInput mi) => !ClickThrough && EventBounds.Contains(mi.Location);

		public override void Draw()
		{
			if (Art == null || Image == null)
				return;

			var source = Source.IsEmpty ? new Rectangle(0, 0, (int)Art.Get(Image).Size.X, (int)Art.Get(Image).Size.Y) : Source;
			var f = GetFrame();
			source = new Rectangle(source.X + f * FrameStride.X, source.Y + f * FrameStride.Y, source.Width, source.Height);
			Igi.DrawSprite(Art.GetRegion(Image, source), RenderBounds.Location);
		}
	}

	/// <summary>The sizes of the interface's standard buttons, in SBTNS.BMP: four states of each (dkreign.exe 0x432110).</summary>
	public enum DrIgiButtonSize { None, Small, Medium, Large }

	/// <summary>
	/// A button of the interface: frames of one image for its states, normal, under the mouse, pressed (or
	/// on) and disabled, and a label in the game's own words. The standard buttons are SBTNS.BMP's: 71, 103
	/// or 153 wide, 22 high, their label centred in FONT12W.
	/// </summary>
	public class DrIgiButtonWidget : DrIgiAreaWidget
	{
		public readonly string Image = null;
		public readonly Rectangle Source = Rectangle.Empty;
		public readonly int2 StateStride = int2.Zero;
		public readonly DrIgiButtonSize Size = DrIgiButtonSize.None;

		[Desc("Frame for each state, in StateStrides from Source; -1 draws the normal frame faded.")]
		public readonly int HoverState = 1;
		public readonly int PressedState = 2;
		public readonly int DisabledState = -1;

		[Desc("The label: a name from the game's strings (MLSTRING.CFG).")]
		public readonly string Label = null;
		public readonly string Font = "FONT12W.PCX";

		[Desc("The label's font when on or under the mouse, for buttons that are only a label.")]
		public readonly string HighlightFont = null;

		public readonly HotkeyReference Key = new();
		public readonly bool RightClick = false;

		public Func<string> GetLabel;
		public Func<bool> IsDisabled = () => false;
		public Func<bool> IsHighlighted = () => false;
		public Action OnClick = () => { };
		public Action OnRightClick = () => { };

		bool depressed;
		string image;
		Rectangle source;
		int2 stride;
		int disabledState;

		public override void Initialize(WidgetArgs args)
		{
			(image, source, stride, disabledState) = Size switch
			{
				DrIgiButtonSize.Small => ("SBTNS.BMP", new Rectangle(0, 0, 71, 22), new int2(71, 0), 3),
				DrIgiButtonSize.Medium => ("SBTNS.BMP", new Rectangle(284, 0, 103, 22), new int2(103, 0), 3),
				DrIgiButtonSize.Large => ("SBTNS.BMP", new Rectangle(696, 0, 153, 22), new int2(153, 0), 3),
				_ => (Image, Source, StateStride, DisabledState),
			};

			if (Area.Width == 0)
				Area = new Rectangle(Area.Location, source.Size);

			base.Initialize(args);
			var label = Label != null ? Igi.Library?.GetString(Label) : null;
			GetLabel ??= () => label;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Button != MouseButton.Left && !(RightClick && mi.Button == MouseButton.Right))
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
				{
					if (mi.Button == MouseButton.Right)
						OnRightClick();
					else
						OnClick();
				}

				return true;
			}

			return false;
		}

		public override bool HandleKeyPress(KeyInput e)
		{
			if (Key.IsActivatedBy(e) && e.Event == KeyInputEvent.Down && !IsDisabled() && IsVisible())
			{
				Game.Sound.PlayNotification(Game.ModData.DefaultRules, null, "Sounds", ChromeMetrics.Get<string>("ClickSound"), null);
				OnClick();
				return true;
			}

			return false;
		}

		public override void Draw()
		{
			if (Art == null)
				return;

			var disabled = IsDisabled();
			var hover = Ui.MouseOverWidget == this && !disabled;
			var state = disabled ? disabledState : (depressed && hover) || IsHighlighted() ? PressedState : hover ? HoverState : 0;
			var faded = state < 0;
			if (faded)
				state = 0;

			var bounds = RenderBounds;
			if (image != null)
			{
				var r = new Rectangle(source.X + state * stride.X, source.Y + state * stride.Y, source.Width, source.Height);
				Igi.DrawSprite(Art.GetRegion(image, r), bounds.Location, faded ? 0.5f : 1f);
			}

			var label = GetLabel();
			if (!string.IsNullOrEmpty(label))
			{
				var font = HighlightFont != null && (IsHighlighted() || hover) ? HighlightFont : Font;
				Igi.DrawTextCentered(Igi.Font(font), label, bounds, disabled ? 0.5f : 1f);
			}
		}
	}

	/// <summary>
	/// The bar across the top of the map view: its ends (TOPBITS.BMP), the buttons' backing (drawn by the
	/// buttons themselves) and the credits display between them. Wider than the original's 448, the
	/// buttons left of the credits stay left, those right of it stay by the panel, the credits keep the
	/// middle and the bar's plate (the column beside each join) fills between.
	/// </summary>
	public class DrIgiTopBarWidget : DrIgiAreaWidget
	{
		public Func<string> GetCredits = () => null;
		public Action OnCreditsDoubleClick = () => { };

		long lastClick;

		public override void Initialize(WidgetArgs args)
		{
			Area = new Rectangle(0, 0, DrIgiWidget.PanelLeft, DrIgiWidget.TopBarHeight);
			Anchor = DrIgiAnchor.Left;
			base.Initialize(args);
		}

		public override void Tick()
		{
			// The bar spans the map view, whatever its width.
			var right = (int)Math.Ceiling(Igi.PanelX);
			var height = (int)Math.Ceiling(DrIgiWidget.TopBarHeight * Scale);
			Bounds = new WidgetBounds(-Parent.RenderOrigin.X, -Parent.RenderOrigin.Y, right, height);
		}

		Rectangle CreditsBox => Igi.ToScreen(new Rectangle(153, 0, 141, DrIgiWidget.TopBarHeight), DrIgiAnchor.Center);

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (!EventBounds.Contains(mi.Location))
				return false;

			if (mi.Event == MouseInputEvent.Down && mi.Button == MouseButton.Left && CreditsBox.Contains(mi.Location))
			{
				var now = Game.RunTime;
				if (now - lastClick < 400)
					OnCreditsDoubleClick();

				lastClick = now;
			}

			return true;
		}

		public override void Draw()
		{
			if (Art == null)
				return;

			const string Bits = "TOPBITS.BMP";
			const string Buttons = "TOPBTNS.BMP";
			var s = Scale;
			var height = DrIgiWidget.TopBarHeight;
			var credits = CreditsBox;
			var leftEnd = 153 * s;
			var rightStart = Igi.ScreenX(294, DrIgiAnchor.Right);

			Igi.DrawSprite(Art.GetRegion(Bits, new Rectangle(0, 0, 6, height)), float2.Zero);
			Igi.DrawSprite(Art.GetRegion(Bits, new Rectangle(147, 0, 7, height)), new float2(Igi.ScreenX(441, DrIgiAnchor.Right), 0));
			Igi.DrawSprite(Art.GetRegion(Bits, new Rectangle(6, 0, 141, height)), new float2(credits.X, 0));

			// The plate between: the last column of the repair button and the first of the attack button.
			if (credits.X > leftEnd + 0.5f)
				Igi.DrawSprite(Art.GetRegion(Buttons, new Rectangle(146, 0, 1, height)), new float2(leftEnd, 0), new float2(credits.X - leftEnd + 1, height * s));

			var creditsRight = credits.X + 141 * s;
			if (rightStart > creditsRight + 0.5f)
				Igi.DrawSprite(Art.GetRegion(Buttons, new Rectangle(147, 0, 1, height)), new float2(creditsRight - 1, 0), new float2(rightStart - creditsRight + 1, height * s));

			var text = GetCredits();
			if (!string.IsNullOrEmpty(text))
				Igi.DrawTextCentered(Igi.Font("FONT16.PCX"), text, credits);
		}
	}

	/// <summary>
	/// The resource bars right of the minimap (RESOBARS.BMP, 52x104, the second frame with the lightning
	/// red): power on the left, water on the right. Each well is filled from the bottom.
	/// </summary>
	public class DrIgiResourceBarsWidget : DrIgiAreaWidget
	{
		public Func<float> GetPower = () => 0;
		public Func<float> GetPowerUsed = () => 0;
		public Func<float> GetWater = () => 0;
		public Func<bool> IsLowPower = () => false;

		public readonly Color PowerColor = Color.FromArgb(255, 72, 200, 72);
		public readonly Color LowPowerColor = Color.FromArgb(255, 208, 48, 32);
		public readonly Color UsedColor = Color.FromArgb(255, 240, 220, 120);
		public readonly Color WaterColor = Color.FromArgb(255, 56, 120, 232);

		public override bool HandleMouseInput(MouseInput mi) => EventBounds.Contains(mi.Location);

		public override void Draw()
		{
			if (Art == null)
				return;

			var low = IsLowPower();
			var origin = RenderBounds.Location;
			Igi.DrawSprite(Art.GetRegion("RESOBARS.BMP", new Rectangle(low ? 52 : 0, 0, 52, 104)), origin);

			Fill(new Rectangle(7, 13, 19, 87), GetPower(), low ? LowPowerColor : PowerColor);
			Fill(new Rectangle(36, 13, 11, 87), GetWater(), WaterColor);

			// The power used, as a line across the power well.
			var used = Math.Clamp(GetPowerUsed(), 0, 1);
			if (used > 0)
			{
				var y = origin.Y + (13 + 87 * (1 - used)) * Scale;
				WidgetUtils.FillRectWithColor(new Rectangle(origin.X + (int)(7 * Scale), (int)y, (int)(19 * Scale), Math.Max(1, (int)Scale)), UsedColor);
			}
		}

		void Fill(Rectangle well, float level, Color color)
		{
			level = Math.Clamp(level, 0, 1);
			if (level <= 0)
				return;

			var origin = RenderBounds.Location;
			var top = well.Y + well.Height * (1 - level);
			var r = Rectangle.FromLTRB(
				origin.X + (int)(well.X * Scale), origin.Y + (int)(top * Scale),
				origin.X + (int)Math.Ceiling(well.Right * Scale), origin.Y + (int)Math.Ceiling(well.Bottom * Scale));
			WidgetUtils.FillRectWithColor(r, color);
		}
	}

	/// <summary>
	/// The team lights above the resource bars (TEAMPIC.BMP): a light for each of eight teams, four to a
	/// row, 13x17, dark when the team is out of the game and lit while it plays.
	/// </summary>
	public class DrIgiTeamLightsWidget : DrIgiAreaWidget
	{
		/// <summary>How bright a team's light is: 0 off, 1 lit, 2 and 3 brighter.</summary>
		public Func<int, int> GetLight = _ => 0;

		/// <summary>A team's colour, which a lit light takes as the original remaps its magenta.</summary>
		public Func<int, Color?> GetColor = _ => null;

		public override bool HandleMouseInput(MouseInput mi) => EventBounds.Contains(mi.Location);

		public override void Draw()
		{
			if (Art == null)
				return;

			// As 0x42d300 draws them: each place in the 4x2 grid has its own frame, four for each brightness.
			var origin = RenderBounds.Location;
			for (var team = 0; team < 8; team++)
			{
				var light = Math.Clamp(GetLight(team), 0, 3);
				var color = light > 0 ? GetColor(team) : null;
				var position = origin + new float2(team % 4 * 13, team / 4 * 17) * Scale;
				var sprite = Art.GetRegion("TEAMPIC.BMP", new Rectangle((light * 4 + team % 4) * 13, team / 4 * 17, 13, 17), color != null);
				var size = sprite.Size.XY * Scale;
				if (color is Color c)
					DrShellArt.DrawQuad(sprite, position, size, new float3(c.R, c.G, c.B) / 255f);
				else
					Igi.DrawSprite(sprite, position);
			}
		}
	}

	/// <summary>
	/// A slider of the MENU tab (zones 80 to 83, 103x15): MESLIDE.BMP's bar, blue, orange under the mouse and
	/// red while dragged, as long as the value. A click or a drag sets it.
	/// </summary>
	public class DrIgiSliderWidget : DrIgiAreaWidget
	{
		public Func<float> GetValue = () => 0;
		public Action<float> OnChange = _ => { };
		public Func<bool> IsDisabled = () => false;

		bool dragging;

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Button != MouseButton.Left || IsDisabled())
				return false;

			if (mi.Event == MouseInputEvent.Down && TakeMouseFocus(mi))
				dragging = true;
			else if (mi.Event == MouseInputEvent.Up && HasMouseFocus)
			{
				dragging = false;
				YieldMouseFocus(mi);
				return true;
			}

			if (!dragging)
				return false;

			var b = RenderBounds;
			OnChange(Math.Clamp((mi.Location.X - b.X) / (float)b.Width, 0, 1));
			return true;
		}

		public override void Draw()
		{
			if (Art == null)
				return;

			var frame = dragging ? 2 : Ui.MouseOverWidget == this ? 1 : 0;
			var width = (int)Math.Round(103 * Math.Clamp(GetValue(), 0, 1));
			if (width > 0)
				Igi.DrawSprite(Art.GetRegion("MESLIDE.BMP", new Rectangle(frame * 103, 0, width, 15)), RenderBounds.Location, IsDisabled() ? 0.5f : 1f);
		}
	}

	/// <summary>
	/// Three cells to choose from, a third of each of the image's three frames (dim, normal, lit): the ORDERS tab's
	/// LOW, MED and HIGH (ORLMH.BMP), the path direction (TRAILMDE.BMP). The setting is lit and the cell under the
	/// mouse normal, lit while pressed, as 0x42d460 draws them.
	/// </summary>
	public class DrIgiLevelWidget : DrIgiAreaWidget
	{
		public readonly string Image = "ORLMH.BMP";

		[Desc("A tip for each cell, from the game's strings.")]
		public readonly string[] Tooltips = [];

		[Desc("The lit cell, 0 to 2; -1 for none (nothing selected, or a selection that differs).")]
		public Func<int> GetValue = () => -1;
		public Action<int> OnSelect = _ => { };
		public Func<bool> IsDisabled = () => false;

		bool pressed;
		string[] tips;

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			tips = Tooltips.Select(t => Igi.Library?.GetString(t)).ToArray();
			GetTooltip = () =>
			{
				var cell = CellAt(Viewport.LastMousePos);
				return cell < tips.Length && !IsDisabled() && !string.IsNullOrEmpty(tips[cell]) ? tips[cell] : null;
			};
		}

		int CellAt(int2 location)
		{
			var b = RenderBounds;
			return Math.Clamp((location.X - b.X) * 3 / Math.Max(1, b.Width), 0, 2);
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Button != MouseButton.Left)
				return false;

			if (mi.Event == MouseInputEvent.Down)
			{
				if (IsDisabled() || !TakeMouseFocus(mi))
					return false;

				pressed = true;
				Game.Sound.PlayNotification(Game.ModData.DefaultRules, null, "Sounds", ChromeMetrics.Get<string>("ClickSound"), null);
				return true;
			}

			if (mi.Event == MouseInputEvent.Up && HasMouseFocus)
			{
				pressed = false;
				YieldMouseFocus(mi);
				if (!IsDisabled() && EventBounds.Contains(mi.Location))
					OnSelect(CellAt(mi.Location));

				return true;
			}

			return false;
		}

		public override void Draw()
		{
			if (Art == null)
				return;

			var b = RenderBounds;
			var size = Art.Get(Image).Size;
			var frameWidth = (int)size.X / 3;
			var height = (int)size.Y;
			var value = IsDisabled() ? -1 : GetValue();
			var hover = Ui.MouseOverWidget == this && !IsDisabled() ? CellAt(Viewport.LastMousePos) : -1;
			for (var i = 0; i < 3; i++)
			{
				var frame = i == value ? 2 : i == hover ? pressed ? 2 : 1 : 0;
				var left = frame * frameWidth + i * frameWidth / 3;
				var source = new Rectangle(left, 0, frame * frameWidth + (i + 1) * frameWidth / 3 - left, height);
				Igi.DrawSprite(Art.GetRegion(Image, source), new float2(b.X + b.Width * i / 3, b.Y));
			}
		}
	}

	/// <summary>The path being laid on the PATHS tab, over the map: a line through its waypoints, each numbered.</summary>
	public class DrIgiPathOverlayWidget : Widget
	{
		readonly WorldRenderer worldRenderer;

		[Desc("The line and markers: the orange of the path direction's art (TRAILMDE.BMP).")]
		public readonly Color Color = Color.FromArgb(255, 248, 152, 32);

		public Func<IReadOnlyList<CPos>> GetPoints = () => [];
		public Func<bool> IsLooped = () => false;

		DrIgiWidget igi;

		[ObjectCreator.UseCtor]
		public DrIgiPathOverlayWidget(WorldRenderer worldRenderer)
		{
			this.worldRenderer = worldRenderer;
			IgnoreMouseOver = true;
		}

		public override bool HandleMouseInput(MouseInput mi) => false;

		public override void Draw()
		{
			var points = GetPoints();
			if (points.Count == 0)
				return;

			if (igi == null)
				for (var w = Parent; w != null && igi == null; w = w.Parent)
					igi = w as DrIgiWidget;

			if (igi?.Art == null)
				return;

			var map = worldRenderer.World.Map;
			var screen = points.Select(c => worldRenderer.Viewport.WorldToViewPx(worldRenderer.ScreenPxPosition(map.CenterOfCell(c)))).ToArray();
			var scale = igi.Scale;
			var lines = Game.Renderer.RgbaColorRenderer;
			for (var i = 1; i < screen.Length; i++)
				lines.DrawLine(new float3(screen[i - 1].X, screen[i - 1].Y, 0), new float3(screen[i].X, screen[i].Y, 0), scale, Color);

			if (IsLooped() && screen.Length > 2)
				lines.DrawLine(new float3(screen[^1].X, screen[^1].Y, 0), new float3(screen[0].X, screen[0].Y, 0), scale, Color);

			// The numbers in OpenRA's bold font with a dark edge: the interface's fonts vanish against the ground.
			var font = Game.Renderer.Fonts["Bold"];
			var size = (int)(3 * scale);
			for (var i = 0; i < screen.Length; i++)
			{
				var p = screen[i];
				WidgetUtils.FillRectWithColor(new Rectangle(p.X - size, p.Y - size, 2 * size, 2 * size), Color);
				var label = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
				var measure = font.Measure(label);
				font.DrawTextWithContrast(label, new float2(p.X - measure.X / 2, p.Y - size - measure.Y - 2), Color, Color.Black, 1);
			}
		}
	}

	/// <summary>The minimap with no picture (no headquarters, or not enough power): the original's static, Static00 to 07.</summary>
	public class DrIgiStaticWidget : DrIgiAreaWidget
	{
		public DrIgiStaticWidget()
		{
			IgnoreMouseOver = true;
		}

		public override void Draw()
		{
			if (Art == null)
				return;

			// The frames are 130x127; the middle fills the minimap.
			var frame = Game.LocalTick / 2 % 8;
			var name = $"STATIC0{frame}.BMP";
			var x = (130 - Area.Width) / 2;
			var y = (127 - Area.Height) / 2;
			Igi.DrawSprite(Art.GetRegion(name, new Rectangle(x, y, Area.Width, Area.Height)), RenderBounds.Location);
		}
	}

	/// <summary>
	/// A box over the map with the original's border (TEXTBRDR.BMP: 24x24 tiles, the corners then the top,
	/// left, right and bottom edges), its inside dark: the original's dialogs and text windows.
	/// </summary>
	public class DrIgiBoxWidget : DrIgiAreaWidget
	{
		public readonly Color Fill = Color.FromArgb(220, 0, 0, 0);
		public readonly string Text = null;
		public readonly int TextTop = 16;

		public Func<string> GetText;

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			var text = Text != null ? Igi.Library?.GetString(Text) : null;
			GetText ??= () => text;
		}

		public override bool HandleMouseInput(MouseInput mi) => EventBounds.Contains(mi.Location);

		public override void Draw()
		{
			if (Art == null)
				return;

			const int T = 24;
			var b = RenderBounds;
			var t = T * Scale;
			WidgetUtils.FillRectWithColor(new Rectangle(b.X + (int)(t / 3), b.Y + (int)(t / 3), b.Width - (int)(2 * t / 3), b.Height - (int)(2 * t / 3)), Fill);

			Sprite Tile(int i) => Art.GetRegion("TEXTBRDR.BMP", new Rectangle(0, i * T, T, T));
			var right = b.Right - t;
			var bottom = b.Bottom - t;
			for (var x = b.X + t; x < right; x += t)
			{
				var w = Math.Min(t, right - x);
				Igi.DrawSprite(Tile(4), new float2(x, b.Y), new float2(w, t));
				Igi.DrawSprite(Tile(7), new float2(x, bottom), new float2(w, t));
			}

			for (var y = b.Y + t; y < bottom; y += t)
			{
				var h = Math.Min(t, bottom - y);
				Igi.DrawSprite(Tile(5), new float2(b.X, y), new float2(t, h));
				Igi.DrawSprite(Tile(6), new float2(right, y), new float2(t, h));
			}

			Igi.DrawSprite(Tile(0), new float2(b.X, b.Y));
			Igi.DrawSprite(Tile(1), new float2(right, b.Y));
			Igi.DrawSprite(Tile(2), new float2(b.X, bottom));
			Igi.DrawSprite(Tile(3), new float2(right, bottom));

			var text = GetText();
			if (!string.IsNullOrEmpty(text))
			{
				var font = Igi.Font("FONT12W.PCX");
				Igi.DrawTextCentered(font, text, new Rectangle(b.X, b.Y + (int)(TextTop * Scale), b.Width, (int)(font.Height * Scale)));
			}
		}
	}

	/// <summary>A line of the game's text (MLSTRING.CFG) in the interface's fonts.</summary>
	public class DrIgiLabelWidget : DrIgiAreaWidget
	{
		public readonly string Text = null;
		public readonly string Font = "FONT12W.PCX";
		public readonly TextAlign Align = TextAlign.Left;

		public Func<string> GetText;

		public DrIgiLabelWidget()
		{
			IgnoreMouseOver = true;
		}

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			var text = Text != null ? Igi.Library?.GetString(Text) : null;
			GetText ??= () => text;
		}

		public override void Draw()
		{
			if (Art == null)
				return;

			var text = GetText();
			if (string.IsNullOrEmpty(text))
				return;

			var font = Igi.Font(Font);
			var b = RenderBounds;
			var width = font.Measure(text) * Scale;
			var x = Align == TextAlign.Center ? b.X + (b.Width - width) / 2 : Align == TextAlign.Right ? b.Right - width : b.X;
			Igi.DrawText(font, text, new float2((int)x, (int)(b.Y + (b.Height - font.Height * Scale) / 2)));
		}
	}
}
