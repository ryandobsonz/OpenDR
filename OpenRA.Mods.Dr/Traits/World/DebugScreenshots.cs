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
using System.Globalization;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Dr.UtilityCommands;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Testing aid for converted campaign missions, idle unless the OPENDR_TEST environment variable is set:",
		"semicolon-separated 'tick:command args' steps. Commands: shot; cash TEAM AMOUNT; killunits TEAM;",
		"killall TEAM; spawn TEAM ACTOR X,Y; explore TEAM; camera X,Y. OPENDR_SCREENSHOT_TICKS=t1,t2 adds shots.")]
	public class DebugScreenshotsInfo : TraitInfo<DebugScreenshots> { }

	public class DebugScreenshots : ITick, IWorldLoaded
	{
		readonly List<(long Tick, string[] Command)> steps = new();
		WorldRenderer worldRenderer;
		long tick;

		public DebugScreenshots()
		{
			foreach (var t in (Environment.GetEnvironmentVariable("OPENDR_SCREENSHOT_TICKS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
				if (long.TryParse(t, out var v))
					steps.Add((v, new[] { "shot" }));

			foreach (var step in (Environment.GetEnvironmentVariable("OPENDR_TEST") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
			{
				var colon = step.IndexOf(':');
				if (colon > 0 && long.TryParse(step[..colon], out var at))
					steps.Add((at, step[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)));
			}
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr) { worldRenderer = wr; }

		void ITick.Tick(Actor self)
		{
			tick++;
			foreach (var (_, command) in steps.Where(s => s.Tick == tick))
			{
				try
				{
					Run(self.World, command);
				}
				catch (Exception e)
				{
					Log.Write("debug", $"OPENDR_TEST {string.Join(' ', command)}: {e.Message}");
				}
			}
		}

		static Player Team(World w, string team) => w.Players.First(p => p.InternalName == ImportDrCampaignCommand.TeamName(int.Parse(team, CultureInfo.InvariantCulture)));

		static CPos Cell(string xy)
		{
			var parts = xy.Split(',');
			return new CPos(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
		}

		void Run(World w, string[] c)
		{
			Log.Write("debug", $"OPENDR_TEST at {tick}: {string.Join(' ', c)}");
			switch (c[0])
			{
				case "shot":
					Game.TakeScreenshot();
					break;

				case "cash":
					Team(w, c[1]).PlayerActor.Trait<PlayerResources>().GiveCash(int.Parse(c[2], CultureInfo.InvariantCulture));
					break;

				case "killunits":
				case "killall":
				{
					var p = Team(w, c[1]);
					foreach (var a in w.Actors.Where(a => a.Owner == p && !a.IsDead && a.IsInWorld && a.Info.HasTraitInfo<HealthInfo>()
						&& (c[0] == "killall" || a.Info.HasTraitInfo<MobileInfo>() || a.Info.HasTraitInfo<AircraftInfo>())).ToList())
						a.Kill(w.WorldActor);
					break;
				}

				case "spawn":
				{
					var p = Team(w, c[1]);
					var cell = Cell(c[3]);
					w.AddFrameEndTask(ww => ww.CreateActor(c[2].ToLowerInvariant(), new TypeDictionary { new LocationInit(cell), new OwnerInit(p) }));
					break;
				}

				case "explore":
					Team(w, c[1]).Shroud.ExploreAll();
					break;

				case "camera":
					worldRenderer?.Viewport.Center(w.Map.CenterOfCell(Cell(c[1])));
					break;
			}
		}
	}
}
