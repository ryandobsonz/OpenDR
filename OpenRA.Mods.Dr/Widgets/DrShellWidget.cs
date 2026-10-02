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
using System.Runtime.CompilerServices;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Dr.FileFormats;
using OpenRA.Mods.Dr.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>A video the shell plays over the whole screen: a cube turn, a briefing opening, a movie.</summary>
	public readonly record struct DrShellClip(string File, DrShellVideoSound Sound = DrShellVideoSound.Effects, int PixelScale = 1);

	/// <summary>
	/// The original game's 640x480 menu screen, scaled to fit the window at its own aspect ratio (or
	/// stretched to fill it) and drawn over black. Its DrShell* descendants are placed in its coordinates.
	/// Between screens it plays the game's videos over everything: the screen before fades into the first,
	/// and the last frame fades into the screen after. A click or a key skips them.
	/// </summary>
	public class DrShellWidget : Widget
	{
		public const int ShellWidth = 640;
		public const int ShellHeight = 480;

		const float FadeIn = 0.15f;
		const float FadeOut = 0.35f;

		static readonly ConditionalWeakTable<ModData, DrShellLibrary> Libraries = new();

		[Desc("Fill the window, giving up the original 4:3 shape.")]
		public readonly bool Stretch = false;

		public Func<string> GetBackground = () => null;
		public Func<KeyInput, bool> OnKeyPress = _ => false;

		public DrShellArt Art { get; private set; }
		public float2 Scale { get; private set; }
		public float2 Origin { get; private set; }

		/// <summary>The original shell art in the installed content, or null if it has not been imported.</summary>
		public static DrShellLibrary Library(ModData modData)
		{
			if (!Libraries.TryGetValue(modData, out var library))
			{
				library = DrShellLibrary.Load(modData.DefaultFileSystem);
				if (library != null)
					Libraries.Add(modData, library);
			}

			return library;
		}

		readonly ModData modData;

		[ObjectCreator.UseCtor]
		public DrShellWidget(ModData modData)
		{
			this.modData = modData;
		}

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			UpdateTransform();

			// Enlarge the art to the next whole multiple of the scale it is drawn at, in real pixels.
			var library = Library(modData);
			var pixels = Math.Max(Scale.X, Scale.Y) * Game.Renderer.WindowScale;
			if (library != null)
				Art = new DrShellArt(library, (int)Math.Ceiling(pixels - 0.01f));

			IsVisible = () => Art != null;
		}

		void UpdateTransform()
		{
			var r = Game.Renderer.Resolution;
			var sx = r.Width / (float)ShellWidth;
			var sy = r.Height / (float)ShellHeight;
			if (!Stretch)
				sx = sy = Math.Min(sx, sy);

			Scale = new float2(sx, sy);
			Origin = new float2((r.Width - ShellWidth * sx) / 2, (r.Height - ShellHeight * sy) / 2);
		}

		public float2 ToScreen(float2 p) => Origin + new float2(p.X * Scale.X, p.Y * Scale.Y);

		public Rectangle ToScreen(Rectangle r)
		{
			var tl = ToScreen(new float2(r.Left, r.Top));
			var br = ToScreen(new float2(r.Right, r.Bottom));
			return Rectangle.FromLTRB((int)tl.X, (int)tl.Y, (int)Math.Ceiling(br.X), (int)Math.Ceiling(br.Y));
		}

		public float2 ToShell(int2 screen) => new((screen.X - Origin.X) / Scale.X, (screen.Y - Origin.Y) / Scale.Y);

		/// <summary>Draws a sprite with its top left at a point of the shell.</summary>
		public void DrawSprite(Sprite sprite, float2 position, float alpha = 1f)
		{
			DrShellArt.DrawQuad(sprite, ToScreen(position), new float2(sprite.Size.X * Scale.X, sprite.Size.Y * Scale.Y), alpha);
		}

		public void DrawText(DrShellFont font, string text, float2 position, float alpha = 1f)
		{
			font.Draw(text, Origin, Scale, position, alpha);
		}

		public override Rectangle RenderBounds => new(0, 0, Game.Renderer.Resolution.Width, Game.Renderer.Resolution.Height);

		public override void Tick()
		{
			UpdateTransform();
			TickClips();
		}

		public override bool HandleKeyPress(KeyInput e)
		{
			if (playing != null)
			{
				if (e.Event == KeyInputEvent.Down && !e.IsRepeat)
					Skip();

				return true;
			}

			return OnKeyPress(e);
		}

		// Videos between screens

		readonly Queue<DrShellClip> clips = new();
		readonly Stopwatch fade = new();
		DrShellVideo playing;
		bool playingMovie;
		DrShellVideo fading;
		Action onClipsDone;
		bool fadingIn;

		/// <summary>True while a video covers the screen, which then neither shows nor takes input.</summary>
		public bool CoversScreen => playing != null && !fadingIn;

		public bool PlayingClips => playing != null;

		/// <summary>Plays videos one after another, then runs onDone; videos missing from the content are left out.</summary>
		public void PlayClips(IEnumerable<DrShellClip> videos, Action onDone)
		{
			SkipClips();
			foreach (var clip in videos)
				clips.Enqueue(clip);

			onClipsDone = onDone;
			fadingIn = true;
			fade.Restart();
			if (!NextClip())
				FinishClips();
		}

		bool NextClip()
		{
			while (clips.Count > 0)
			{
				var clip = clips.Dequeue();
				var video = DrShellVideo.Open(clip.File, Art.Upscale, sound: clip.Sound, pixelScale: clip.PixelScale);
				if (video == null)
					continue;

				playing?.Dispose();
				playing = video;
				playingMovie = clip.Sound == DrShellVideoSound.Video;
				playing.Play();
				return true;
			}

			return false;
		}

		/// <summary>A click or a key: skips a movie to what comes after it, or the menus' videos to the next screen.</summary>
		public void Skip()
		{
			if (playing == null)
				return;

			if (!playingMovie || clips.Count == 0)
			{
				SkipClips();
				return;
			}

			playing.End();
			if (!NextClip())
				FinishClips();
		}

		/// <summary>Ends the videos at once, on the last frame of the one playing.</summary>
		public void SkipClips()
		{
			if (playing == null)
				return;

			clips.Clear();
			playing.End();
			FinishClips();
		}

		void FinishClips()
		{
			fading?.Dispose();
			fading = playing;
			playing = null;
			fadingIn = false;
			fade.Restart();

			var done = onClipsDone;
			onClipsDone = null;
			done?.Invoke();
		}

		/// <summary>
		/// Moves on to the next video, or the next screen, in Tick: showing a screen can load a map, which
		/// must not happen mid-frame. Draw only brings the frame up to the time.
		/// </summary>
		void TickClips()
		{
			if (playing == null)
			{
				if (fading != null && fade.Elapsed.TotalSeconds > FadeOut)
				{
					fading.Dispose();
					fading = null;
				}

				return;
			}

			if (fadingIn && fade.Elapsed.TotalSeconds > FadeIn)
				fadingIn = false;

			playing.Update();
			if (playing.Finished && !NextClip())
				FinishClips();
		}

		// The shell's widgets let input through while a video plays (DrShellAreaWidget.Blocked).
		public override bool HandleMouseInput(MouseInput mi)
		{
			if (playing == null)
				return false;

			if (mi.Event == MouseInputEvent.Down)
				Skip();

			return true;
		}

		void DrawClip(DrShellVideo video, float alpha)
		{
			DrShellArt.DrawQuad(video.Sprite, Origin, new float2(video.Size.X * Scale.X, video.Size.Y * Scale.Y), alpha);
		}

		public override void DrawOuter()
		{
			if (!IsVisible())
				return;

			base.DrawOuter();

			playing?.Update();
			if (playing != null)
				DrawClip(playing, fadingIn ? Math.Clamp((float)fade.Elapsed.TotalSeconds / FadeIn, 0, 1) : 1);
			else if (fading != null)
				DrawClip(fading, 1 - Math.Clamp((float)fade.Elapsed.TotalSeconds / FadeOut, 0, 1));
		}

		public override void Draw()
		{
			UpdateTransform();
			WidgetUtils.FillRectWithColor(RenderBounds, Color.Black);

			var background = GetBackground();
			if (!CoversScreen && background != null && Art.Contains(background))
				DrawSprite(Art.Get(background), float2.Zero);
		}

		public override void Removed()
		{
			base.Removed();
			clips.Clear();
			playing?.Dispose();
			fading?.Dispose();
			playing = fading = null;
			Art?.Dispose();
			Art = null;
		}
	}

	/// <summary>A widget placed by a rectangle of its DrShellWidget ancestor's 640x480 screen.</summary>
	public abstract class DrShellAreaWidget : Widget
	{
		[Desc("Left, top, width and height in the original 640x480 screen.")]
		public Rectangle Area = Rectangle.Empty;

		DrShellWidget shell;

		protected DrShellWidget Shell
		{
			get
			{
				if (shell == null)
					for (var w = Parent; w != null && shell == null; w = w.Parent)
						shell = w as DrShellWidget;

				return shell;
			}
		}

		public override Rectangle RenderBounds => Shell.ToScreen(Area);
		public override int2 RenderOrigin => RenderBounds.Location;

		/// <summary>True while the shell plays a video between screens; the screen's widgets then take no input.</summary>
		protected bool Blocked => Shell.PlayingClips;
	}
}
