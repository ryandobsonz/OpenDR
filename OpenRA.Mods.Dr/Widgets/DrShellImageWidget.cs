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

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>A frame of an image from the original shell, drawn at its area's top left.</summary>
	public class DrShellImageWidget : DrShellAreaWidget
	{
		public readonly string Image = null;
		public readonly int Frames = 1;
		public readonly bool Horizontal = false;

		[Desc("Draw colour 0 too, which is otherwise transparent.")]
		public readonly bool Opaque = false;

		[Desc("The frame to draw; a negative one draws nothing.")]
		public Func<int> GetFrame = () => 0;

		public override void Draw()
		{
			var frame = GetFrame();
			if (Image != null && frame >= 0)
				Shell.DrawSprite(Shell.Art.GetFrames(Image, Frames, Horizontal, Opaque)[frame], new float2(Area.X, Area.Y));
		}
	}
}
