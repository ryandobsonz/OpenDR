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
using System.Runtime.CompilerServices;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Dr.FileFormats;
using OpenRA.Mods.Dr.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>
	/// The original game's 640x480 menu screen, scaled to fit the window at its own aspect ratio (or
	/// stretched to fill it) and drawn over black. Its DrShell* descendants are placed in its coordinates.
	/// </summary>
	public class DrShellWidget : Widget
	{
		public const int ShellWidth = 640;
		public const int ShellHeight = 480;

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
		}

		public override bool HandleKeyPress(KeyInput e) => OnKeyPress(e);

		public override void Draw()
		{
			UpdateTransform();
			WidgetUtils.FillRectWithColor(RenderBounds, Color.Black);

			var background = GetBackground();
			if (background != null && Art.Contains(background))
				DrawSprite(Art.Get(background), float2.Zero);
		}

		public override void Removed()
		{
			base.Removed();
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
	}
}
