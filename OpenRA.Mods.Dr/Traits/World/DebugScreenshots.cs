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
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Testing aid: screenshots the game at the world ticks listed in the OPENDR_SCREENSHOT_TICKS environment variable,",
		"comma separated. Does nothing when it is unset.")]
	public class DebugScreenshotsInfo : TraitInfo<DebugScreenshots> { }

	public class DebugScreenshots : ITick
	{
		readonly long[] ticks = (Environment.GetEnvironmentVariable("OPENDR_SCREENSHOT_TICKS") ?? "")
			.Split(',', StringSplitOptions.RemoveEmptyEntries)
			.Select(t => long.TryParse(t, out var v) ? v : -1)
			.Where(v => v >= 0)
			.ToArray();

		long tick;

		void ITick.Tick(Actor self)
		{
			if (ticks.Contains(++tick))
				Game.TakeScreenshot();
		}
	}
}
