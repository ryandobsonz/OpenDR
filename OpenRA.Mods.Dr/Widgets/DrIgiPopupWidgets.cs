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
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Dr.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>
	/// A list in one of the interface's popups (the Load/Save popup's saved games): a line each in FONT12W, the
	/// chosen one lit in FONT12T over a dark bar. A click chooses, a double click activates; the wheel scrolls.
	/// </summary>
	public class DrIgiListWidget : DrIgiAreaWidget
	{
		public readonly string Font = "FONT12W.PCX";
		public readonly string SelectedFont = "FONT12T.PCX";
		public readonly Color SelectedFill = Color.FromArgb(255, 24, 48, 56);

		[Desc("Original pixels from one line to the next.")]
		public readonly int LineHeight = 13;

		public Func<IReadOnlyList<string>> GetItems = () => [];
		public Func<int> GetSelected = () => -1;
		public Action<int> OnSelect = _ => { };
		public Action<int> OnActivate = _ => { };

		int scroll;

		int VisibleLines => Math.Max(1, Area.Height / LineHeight);

		public bool CanScrollUp => scroll > 0;
		public bool CanScrollDown => scroll + VisibleLines < GetItems().Count;

		public void Scroll(int lines) => scroll = Math.Max(0, Math.Min(scroll + lines, GetItems().Count - VisibleLines));

		public void ScrollToTop() => scroll = 0;

		int ItemAt(int2 screen)
		{
			var b = RenderBounds;
			if (!b.Contains(screen))
				return -1;

			var i = scroll + (int)((screen.Y - b.Y) / (LineHeight * Scale));
			return i < GetItems().Count ? i : -1;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (!RenderBounds.Contains(mi.Location))
				return false;

			if (mi.Event == MouseInputEvent.Scroll)
			{
				Scroll(-Math.Sign(mi.Delta.Y));
				return true;
			}

			if (mi.Button != MouseButton.Left || mi.Event != MouseInputEvent.Down)
				return true;

			var item = ItemAt(mi.Location);
			if (item >= 0)
			{
				OnSelect(item);
				if (mi.MultiTapCount >= 2)
					OnActivate(item);
			}

			return true;
		}

		public override void Draw()
		{
			if (Art == null)
				return;

			var items = GetItems();
			scroll = Math.Max(0, Math.Min(scroll, items.Count - VisibleLines));
			var selected = GetSelected();
			var normal = Igi.Font(Font);
			var lit = Igi.Font(SelectedFont);
			var b = RenderBounds;
			var line = LineHeight * Scale;
			for (var l = 0; l < VisibleLines && scroll + l < items.Count; l++)
			{
				var i = scroll + l;
				var y = b.Y + l * line;
				var font = i == selected ? lit : normal;
				if (i == selected)
					WidgetUtils.FillRectWithColor(new Rectangle(b.X, (int)y, b.Width, (int)Math.Ceiling(line)), SelectedFill);

				Igi.DrawText(font, Fit(font, items[i], Area.Width - 4), new float2(b.X + 2 * Scale, (int)(y + (line - font.Height * Scale) / 2)));
			}
		}

		/// <summary>The text, cut short with an ellipsis where it would run past the width (original pixels).</summary>
		static string Fit(DrShellFont font, string text, int width)
		{
			if (font.Measure(text) <= width)
				return text;

			while (text.Length > 0 && font.Measure(text + "...") > width)
				text = text[..^1];

			return text + "...";
		}
	}

	/// <summary>
	/// A line of text to type into, in one of the interface's popups (the Load/Save popup's name): FONT12W over a
	/// dark field, with a caret while it has the keyboard. Enter and Escape go to their actions.
	/// </summary>
	public class DrIgiTextFieldWidget : DrIgiAreaWidget
	{
		public readonly string Font = "FONT12W.PCX";
		public readonly Color Fill = Color.FromArgb(255, 8, 12, 16);
		public readonly Color Border = Color.FromArgb(255, 72, 80, 88);
		public readonly int MaxLength = 32;

		public string Text = "";
		public Action OnEnter = () => { };
		public Action OnEscape = () => { };
		public Func<bool> IsDisabled = () => false;

		long blinkStart;

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (!RenderBounds.Contains(mi.Location))
				return false;

			if (mi.Event == MouseInputEvent.Down && mi.Button == MouseButton.Left && !IsDisabled())
			{
				TakeKeyboardFocus();
				blinkStart = Game.RunTime;
			}

			return true;
		}

		public override bool HandleKeyPress(KeyInput e)
		{
			if (!HasKeyboardFocus || e.Event != KeyInputEvent.Down)
				return HasKeyboardFocus;

			blinkStart = Game.RunTime;
			switch (e.Key)
			{
				case Keycode.RETURN:
				case Keycode.KP_ENTER:
					OnEnter();
					break;
				case Keycode.ESCAPE:
					YieldKeyboardFocus();
					OnEscape();
					break;
				case Keycode.BACKSPACE:
					if (Text.Length > 0)
						Text = Text[..^1];
					break;
			}

			// The keyboard is the field's while it has it: no hotkeys fire under the typing.
			return true;
		}

		public override bool HandleTextInput(string text)
		{
			if (!HasKeyboardFocus || IsDisabled())
				return false;

			// Only what a file name can hold.
			foreach (var c in text)
				if (Text.Length < MaxLength && (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_'))
					Text += c;

			blinkStart = Game.RunTime;
			return true;
		}

		public override void Draw()
		{
			if (Art == null)
				return;

			var b = RenderBounds;
			WidgetUtils.FillRectWithColor(b, Border);
			var s = (int)Math.Max(1, Scale);
			WidgetUtils.FillRectWithColor(new Rectangle(b.X + s, b.Y + s, b.Width - 2 * s, b.Height - 2 * s), Fill);

			var font = Igi.Font(Font);
			var text = Text;
			while (text.Length > 0 && font.Measure(text + "_") > Area.Width - 6)
				text = text[1..];

			var position = new float2(b.X + 3 * Scale, (int)(b.Y + (b.Height - font.Height * Scale) / 2));
			Igi.DrawText(font, text, position, IsDisabled() ? 0.5f : 1f);
			if (HasKeyboardFocus && (Game.RunTime - blinkStart) % 1000 < 500)
				Igi.DrawText(font, "_", position + new float2(font.Measure(text) * Scale, 0));
		}
	}

	/// <summary>
	/// Text in one of the interface's windows (Restate Objective's briefing), in FONT12W, wrapped to its area and
	/// scrolled by the wheel. It reads the briefing files' markup: \n breaks a line, \c centres it, \s is a space.
	/// </summary>
	public class DrIgiTextWidget : DrIgiAreaWidget
	{
		public readonly string Font = "FONT12W.PCX";

		[Desc("Extra original pixels between lines.")]
		public readonly int LineSpacing = 2;

		readonly List<(string Text, bool Centered)> lines = [];
		string source;
		int scroll;

		DrShellFont TextFont => Igi.Font(Font);
		int LineHeight => TextFont.Height + LineSpacing;
		int VisibleLines => Math.Max(1, Area.Height / LineHeight);

		public bool CanScrollUp => scroll > 0;
		public bool CanScrollDown => scroll + VisibleLines < lines.Count;

		public void SetText(string markup)
		{
			source = markup ?? "";
			scroll = 0;
			lines.Clear();
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
				var p = paragraph.Replace("\\c", "").Replace("\\s", " ").Trim();
				Wrap(p, centered);
			}

			while (lines.Count > 0 && lines[^1].Text.Length == 0)
				lines.RemoveAt(lines.Count - 1);
		}

		void Wrap(string paragraph, bool centered)
		{
			var line = "";
			foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				var candidate = line.Length == 0 ? word : line + " " + word;
				if (TextFont.Measure(candidate) > Area.Width && line.Length > 0)
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
			if (mi.Event != MouseInputEvent.Scroll || !RenderBounds.Contains(mi.Location))
				return false;

			Scroll(-Math.Sign(mi.Delta.Y) * 3);
			return true;
		}

		public override void Draw()
		{
			if (Art == null || source == null)
				return;

			// Laid out once the art (the font) is at hand.
			if (lines.Count == 0 && source.Length > 0)
				Layout();

			var b = RenderBounds;
			Game.Renderer.EnableScissor(b);
			var y = (float)b.Y;
			for (var i = scroll; i < lines.Count && i < scroll + VisibleLines; i++)
			{
				var (text, centered) = lines[i];
				var x = centered ? b.X + (Area.Width - TextFont.Measure(text)) * Scale / 2 : b.X;
				Igi.DrawText(TextFont, text, new float2((int)x, (int)y));
				y += LineHeight * Scale;
			}

			Game.Renderer.DisableScissor();
		}
	}
}
