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
	/// <summary>
	/// The mission ring around the Encryption Key on the cube's main face, read as a clock: mission N is the
	/// disc at N o'clock, mission 12 the gate at the top, and the key in the middle the Togran's mission. A
	/// disc shows which sides have won its mission with the frames of m_state; locked missions are darkened.
	/// The key is a video in the hole the face leaves for it (GetKeyVideo: the file, and whether it loops).
	/// </summary>
	public class DrShellRingWidget : DrShellAreaWidget
	{
		static readonly int2 KeyVideoOrigin = new(248, 190);

		// Top left of each disc's 20x16 marker in the missions screen, from one o'clock to eleven.
		static readonly int2[] Discs =
		[
			new(354, 185), new(383, 215), new(393, 258), new(381, 299), new(349, 327), new(307, 339),
			new(267, 325), new(238, 295), new(228, 252), new(240, 211), new(272, 181)
		];

		static readonly Rectangle Gate = new(297, 163, 44, 32);
		static readonly Rectangle Key = new(258, 200, 120, 120);

		public readonly string Image = "m_state";

		public Func<int, bool> IsUnlocked = _ => true;
		public Func<int, int> GetState = _ => 0;
		public Func<int> GetSelected = () => 0;
		public Action<int> OnSelect = _ => { };
		public Func<(string File, bool Loop)> GetKeyVideo = () => (null, false);

		int hover;
		int ticks;
		DrShellVideo key;
		(string File, bool Loop) keyVideo;

		/// <summary>Starts the key's video again, as when the face comes into view.</summary>
		public void RestartKey()
		{
			key?.Dispose();
			key = null;
			keyVideo = default;
		}

		void DrawKey()
		{
			var wanted = GetKeyVideo();
			if (wanted != keyVideo)
			{
				RestartKey();
				keyVideo = wanted;
				if (wanted.File != null)
				{
					key = DrShellVideo.Open(wanted.File, Shell.Art.Upscale, wanted.Loop, circle: true);
					key?.Play();
				}
			}

			if (key == null)
				return;

			key.Update();
			Shell.DrawSprite(key.Sprite, KeyVideoOrigin);
		}

		public override void Removed()
		{
			base.Removed();
			RestartKey();
		}

		static Rectangle Slot(int mission)
		{
			if (mission == DrCampaign.Togran)
				return Key;

			if (mission == DrCampaign.Missions)
				return Gate;

			var d = Discs[mission - 1];
			return new Rectangle(d.X - 4, d.Y - 5, 28, 26);
		}

		int SlotAt(int2 screen)
		{
			var p = Shell.ToShell(screen);
			for (var m = 1; m <= DrCampaign.Togran; m++)
			{
				var r = Slot(m);
				if (m == DrCampaign.Togran)
				{
					var c = new float2(r.X + r.Width / 2f, r.Y + r.Height / 2f);
					if ((p - c).LengthSquared <= r.Width * r.Width / 4f)
						return m;
				}
				else if (r.Contains((int)p.X, (int)p.Y))
					return m;
			}

			return 0;
		}

		public override void Tick()
		{
			ticks++;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (Blocked)
				return false;

			if (mi.Event == MouseInputEvent.Move)
			{
				hover = SlotAt(mi.Location);
				return false;
			}

			if (mi.Button != MouseButton.Left || mi.Event != MouseInputEvent.Down)
				return false;

			var slot = SlotAt(mi.Location);
			if (slot == 0 || !IsUnlocked(slot))
				return false;

			Game.Sound.PlayNotification(Game.ModData.DefaultRules, null, "Sounds", ChromeMetrics.Get<string>("ClickSound"), null);
			OnSelect(slot);
			return true;
		}

		public override void MouseExited()
		{
			hover = 0;
		}

		/// <summary>An ellipse of rows a pixel apart, which leave no gaps when the interface is scaled.</summary>
		static void FillEllipse(Rectangle r, Color color)
		{
			var cx = r.X + r.Width / 2f;
			var cy = r.Y + r.Height / 2f;
			for (var y = r.Top; y < r.Bottom; y++)
			{
				var dy = (y + 0.5f - cy) / (r.Height / 2f);
				if (dy * dy >= 1)
					continue;

				var dx = r.Width / 2f * MathF.Sqrt(1 - dy * dy);
				Game.Renderer.RgbaColorRenderer.FillRect(new float3(cx - dx, y, 0), new float3(cx + dx, y + 1, 0), color);
			}
		}

		public override void Draw()
		{
			DrawKey();
			var frames = Shell.Art.GetFrames(Image, 4);
			var selected = GetSelected();
			var blink = ticks / 12 % 2 == 0;
			for (var m = 1; m <= DrCampaign.Missions; m++)
			{
				var r = Slot(m);
				if (m < DrCampaign.Missions)
				{
					var state = GetState(m);

					// The selected mission blinks between its state and every side lit.
					if (m == selected && blink)
						state = 3;

					Shell.DrawSprite(frames[state], new float2(Discs[m - 1].X, Discs[m - 1].Y));
				}

				if (!IsUnlocked(m))
					FillEllipse(Shell.ToScreen(r), Color.FromArgb(190, 0, 0, 0));
				else if (m == hover || (m == selected && m == DrCampaign.Missions && blink))
					FillEllipse(Shell.ToScreen(r), Color.FromArgb(50, 255, 200, 120));
			}

			// The key's own video shows it selected; a lit key needs no blink.
			if (IsUnlocked(DrCampaign.Togran) && hover == DrCampaign.Togran && selected != DrCampaign.Togran)
				FillEllipse(Shell.ToScreen(Key), Color.FromArgb(40, 255, 200, 120));
		}
	}
}
