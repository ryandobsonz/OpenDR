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
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Dr.UtilityCommands;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Testing aid for converted campaign missions, idle unless the OPENDR_TEST environment variable is set:",
		"semicolon-separated 'tick:command args' steps. Commands: shot; leave; save NAME; resume; cash TEAM AMOUNT; killunits TEAM;",
		"killall TEAM; kill NAME; killtype TEAM ACTOR; steal INFILTRATOR TARGET; select NAME; press WIDGET; clickui X,Y; rclickui X,Y; teleport NAME X,Y; spawn TEAM ACTOR X,Y [NAME]; order NAME ORDER TARGETNAME; explore TEAM; camera X,Y.",
		"Names are the map's actor names (u<id> for original units) or those given to spawn. OPENDR_SCREENSHOT_TICKS=t1,t2 adds shots.")]
	public class DebugScreenshotsInfo : TraitInfo<DebugScreenshots> { }

	public class DebugScreenshots : ITick, IWorldLoaded, IGameOver
	{
		readonly List<(long Tick, string[] Command)> steps = new();
		readonly Dictionary<string, Actor> spawned = new();
		WorldRenderer worldRenderer;
		long tick;
		bool realTime;

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

		/// <summary>The game pauses when it ends; the steps still to come then run a second apart.</summary>
		void IGameOver.GameOver(World world) => RunRestInRealTime(world, 2000);

		/// <summary>The steps after this tick, a second apart in real time: the world has stopped ticking.</summary>
		void RunRestInRealTime(World world, int delay)
		{
			realTime = true;
			foreach (var (_, command) in steps.Where(s => s.Tick > tick).OrderBy(s => s.Tick))
			{
				var c = command;
				Game.RunAfterDelay(delay, () =>
				{
					if (Game.IsCurrentWorld(world))
						Run(world, c);
				});

				delay += 1000;
			}
		}

		void ITick.Tick(Actor self)
		{
			if (realTime)
				return;

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

			// A step that opened a window pausing the game (the settings, the in-game menu) stops the ticks too.
			if (self.World.PredictedPaused && steps.Any(s => s.Tick > tick))
				RunRestInRealTime(self.World, 1000);
		}

		static Player Team(World w, string team) => w.Players.First(p => p.InternalName == ImportDrCampaignCommand.TeamName(int.Parse(team, CultureInfo.InvariantCulture)));

		Actor Named(World w, string name)
		{
			if (spawned.TryGetValue(name, out var a))
				return a;

			return w.WorldActor.Trait<SpawnMapActors>().Actors[name];
		}

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

				case "save":
					// As the in-game menu's Save Game does.
					w.RequestGameSave(c[1] + ".orasav", false);
					break;

				case "resume":
					// A loaded game opens paused, under the in-game menu: its Resume, a second later in real time.
					Game.RunAfterDelay(1000, () => Ui.Root.GetOrNull<ButtonWidget>("RESUME")?.OnClick());
					break;

				case "leave":
					// As the in-game menu's Leave does: back to the menus.
					Game.RunAfterTick(() =>
					{
						Game.Disconnect();
						Ui.ResetAll();
						Game.LoadShellMap();
					});
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
					w.AddFrameEndTask(ww =>
					{
						var a = ww.CreateActor(c[2].ToLowerInvariant(), new TypeDictionary { new LocationInit(cell), new OwnerInit(p) });
						if (c.Length > 4)
							spawned[c[4]] = a;
					});
					break;
				}

				case "order":
				{
					var actor = Named(w, c[1]);
					var target = Named(w, c[3]);
					w.IssueOrder(new Order(c[2], actor, Target.FromActor(target), false));
					break;
				}

				case "steal":
				{
					// As a completed infiltration would, without the walk in.
					var infiltrator = Named(w, c[1]);
					var target = Named(w, c[2]);
					foreach (var n in target.TraitsImplementing<INotifyInfiltrated>())
						n.Infiltrated(target, infiltrator, default);
					break;
				}

				case "kill":
					Named(w, c[1]).Kill(w.WorldActor);
					break;

				case "teleport":
				{
					var a = Named(w, c[1]);
					var cell = Cell(c[2]);
					w.AddFrameEndTask(_ => a.Trait<IPositionable>().SetPosition(a, cell));
					break;
				}

				case "select":
					w.Selection.Combine(w, new[] { Named(w, c[1]) }, false, false);
					break;

				case "clickui":
				case "rclickui":
				{
					// A click at a point of the screen, in the interface's pixels (the window's over its UI scale), through its input path.
					var xy = c[1].Split(',');
					var at = new int2(int.Parse(xy[0], CultureInfo.InvariantCulture), int.Parse(xy[1], CultureInfo.InvariantCulture));
					var button = c[0] == "rclickui" ? MouseButton.Right : MouseButton.Left;
					Game.RunAfterTick(() => Sync.RunUnsynced(w, () =>
					{
						Ui.HandleInput(new MouseInput(MouseInputEvent.Move, MouseButton.None, at, int2.Zero, Modifiers.None, 0));
						var down = Ui.HandleInput(new MouseInput(MouseInputEvent.Down, button, at, int2.Zero, Modifiers.None, 1));
						Ui.HandleInput(new MouseInput(MouseInputEvent.Up, button, at, int2.Zero, Modifiers.None, 1));
						Log.Write("debug", $"{c[0]} {at}: over {Ui.MouseOverWidget?.Id ?? "nothing"}, handled {down}");
					}));
					break;
				}

				case "press":
					// A button of the in-game interface, or of an OpenRA window, by its widget id, as a click on it does.
					Game.RunAfterTick(() => Sync.RunUnsynced(w, () =>
					{
						var button = Ui.Root.GetOrNull(c[1]);
						if (button is Widgets.DrIgiButtonWidget igi)
							igi.OnClick();
						else if (button is ButtonWidget b)
							b.OnClick();
					}));
					break;

				case "key":
				{
					// A key as the keyboard sends it, by its hotkey name and modifiers: "key A Shift", "key F1".
					var hotkey = FieldLoader.GetValue<Hotkey>("key", string.Join(' ', c[1..]));
					Game.RunAfterTick(() => Sync.RunUnsynced(w, () =>
					{
						foreach (var e in new[] { KeyInputEvent.Down, KeyInputEvent.Up })
							Ui.HandleKeyPress(new KeyInput { Event = e, Key = hotkey.Key, Modifiers = hotkey.Modifiers, MultiTapCount = 1 });
					}));
					break;
				}

				case "killtype":
				{
					var p = Team(w, c[1]);
					foreach (var a in w.Actors.Where(a => a.Owner == p && !a.IsDead && a.IsInWorld && a.Info.Name == c[2].ToLowerInvariant()).ToList())
						a.Kill(w.WorldActor);
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
