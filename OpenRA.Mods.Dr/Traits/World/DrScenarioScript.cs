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
using System.IO;
using System.Linq;
using System.Text;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Dr.FileFormats;
using OpenRA.Mods.Dr.Scripting;
using OpenRA.Graphics;
using OpenRA.Mods.Dr.UtilityCommands;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Runs a campaign mission converted by --import-dr-campaign: the original scenario's special forces, patrols,",
		"AI trees (.fsm) and end-condition trees (.end), as the game's AIP manual describes them.")]
	public class DrScenarioScriptInfo : TraitInfo
	{
		[Desc("Dark Reign game cycles per OpenRA tick. The manual gives 15 to 40 cycles a second.")]
		public readonly int CyclesPerTick = 1;

		[Desc("Ticks between condition checks.")]
		public readonly int EvaluateInterval = 4;

		[Desc("Lobby option holding the difficulty, which picks the easy, medium or hard AI tree.")]
		public readonly string DifficultyOption = "difficulty";

		public override object Create(ActorInitializer init) { return new DrScenarioScript(init.Self, this); }
	}

	public class DrScenarioScript : ITick, IWorldLoaded, IDrScenarioContext
	{
		readonly DrScenarioScriptInfo info;
		readonly World world;

		DrScenario scenario;
		readonly Dictionary<int, Player> teams = new();
		readonly Dictionary<int, Actor> actorsById = new();
		readonly Dictionary<Actor, int> idsByActor = new();
		readonly Dictionary<int, int> startUnits = new();
		readonly Dictionary<int, int> startBuildings = new();
		readonly Dictionary<(int Team, string Type), int> built = new();
		readonly List<DrConditionTree> trees = new();
		readonly HashSet<Actor> specialForces = new();
		readonly Dictionary<Actor, (DrPatrol Patrol, int Next, int Step)> patrols = new();
		readonly Dictionary<string, string> messages = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<int, string> briefing = new();
		readonly List<int> objectives = new();

		/// <summary>Region priorities set by AdjustRegionPri, per team, for the strategic AI: region id to (priority, min, max).</summary>
		public readonly Dictionary<int, Dictionary<int, (int Priority, int Min, int Max)>> RegionPriorities = new();

		bool started;
		bool finished;
		long ticks;

		public DrScenarioScript(Actor self, DrScenarioScriptInfo info)
		{
			this.info = info;
			world = self.World;
		}

		public long Cycle => ticks * info.CyclesPerTick;

		public DrScenario Scenario => scenario;

		public bool IsSpecialForce(Actor a) => specialForces.Contains(a);

		public Player TeamPlayer(int team) => teams.GetValueOrDefault(team);

		public int TeamOf(Player p) => teams.FirstOrDefault(kv => kv.Value == p).Key;

		/// <summary>Raised when a team's FSM switches AIP, for the team's strategic AI.</summary>
		public event Action<int, string> AipChanged = (_, _) => { };

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			// Open on the player's start location, else their first building.
			var scn = w.Map.Package.Contents.FirstOrDefault(f => f.EndsWith(".scn", StringComparison.OrdinalIgnoreCase));
			if (scn == null)
				return;

			DrScenario s;
			using (var stream = w.Map.Open(scn))
				s = new DrScenario(stream);

			if (s.Teams.TryGetValue(0, out var team) && (team.StartX > 0 || team.StartY > 0))
			{
				wr.Viewport.Center(w.Map.CenterOfCell(DrScenario.PixelToCell(team.StartX, team.StartY)));
				return;
			}

			var first = s.Placements.FirstOrDefault(p => p.Team == 0 && !p.IsThing);
			if (first != null)
				wr.Viewport.Center(w.Map.CenterOfCell(DrScenario.TileToCell(first.X, first.Y)));
		}

		void IDrScenarioContext.Trace(string message)
		{
			Log.Write("drscenario", $"{Cycle,7} {message}");
		}

		void Start()
		{
			Log.AddChannel("drscenario", "drscenario.log");
			var map = world.Map;
			var scn = map.Package.Contents.FirstOrDefault(f => f.EndsWith(".scn", StringComparison.OrdinalIgnoreCase));
			if (scn == null)
				throw new InvalidDataException("DrScenarioScript: the map has no .scn file");

			using (var s = map.Open(scn))
				scenario = new DrScenario(s);

			for (var i = 0; i < 9; i++)
			{
				var p = world.Players.FirstOrDefault(pl => pl.InternalName == ImportDrCampaignCommand.TeamName(i));
				if (p != null)
					teams[i] = p;
			}

			var mapActors = world.WorldActor.Trait<SpawnMapActors>().Actors;
			foreach (var kv in mapActors)
			{
				if (kv.Key.Length > 1 && kv.Key[0] == 'u' && int.TryParse(kv.Key.AsSpan(1), out var id))
				{
					actorsById[id] = kv.Value;
					idsByActor[kv.Value] = id;
				}
			}

			foreach (var t in scenario.Teams.Values)
			{
				if (!teams.TryGetValue(t.Index, out var p))
					continue;

				var resources = p.PlayerActor.Trait<PlayerResources>();
				resources.Cash = t.Credits;
				startUnits[t.Index] = Units(p).Count();
				startBuildings[t.Index] = Buildings(p).Count();
			}

			world.ActorAdded += OnActorAdded;

			foreach (var sf in scenario.SpecialForces.Values)
				foreach (var id in sf.UnitIds)
					if (actorsById.TryGetValue(id, out var a))
						specialForces.Add(a);

			foreach (var kv in scenario.Patrols)
				if (kv.Value.Points.Count > 0 && actorsById.TryGetValue(kv.Key, out var a) && a.Info.HasTraitInfo<IMoveInfo>())
					patrols[a] = (kv.Value, 0, 1);

			LoadMessages(map);
			LoadBriefing(map);
			AddObjectives();
			LoadTrees(map);

			foreach (var tree in trees)
				tree.Start(this);
		}

		void LoadMessages(Map map)
		{
			if (!map.Package.Contains("messages.txt"))
				return;

			using (var s = map.Open("messages.txt"))
			using (var reader = new StreamReader(s, Encoding.UTF8))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					var tab = line.IndexOf('\t');
					if (tab > 0)
						messages[line[..tab]] = line[(tab + 1)..];
				}
			}
		}

		void LoadBriefing(Map map)
		{
			var brf = map.Package.Contents.FirstOrDefault(f => f.EndsWith(".brf", StringComparison.OrdinalIgnoreCase));
			if (brf == null)
				return;

			using (var s = map.Open(brf))
			using (var reader = new StreamReader(s, Encoding.Latin1))
				briefing = ImportDrCampaignCommand.BriefingSections(reader.ReadToEnd());
		}

		/// <summary>The briefing's "-->" lines become the objectives; the end tree, not they, decides the game.</summary>
		void AddObjectives()
		{
			if (!teams.TryGetValue(0, out var player))
				return;

			var mo = player.PlayerActor.TraitOrDefault<MissionObjectives>();
			if (mo == null)
				return;

			var lines = briefing.GetValueOrDefault(1, "").Split("\\n")
				.Select(l => l.Trim())
				.ToList();

			// Objectives can wrap over several lines; continuation lines are indented and lack the arrow.
			var current = new StringBuilder();
			foreach (var l in lines)
			{
				if (l.StartsWith("-->", StringComparison.Ordinal))
				{
					if (current.Length > 0)
						objectives.Add(mo.Add(player, current.ToString(), "Primary", inhibitAnnouncement: true));
					current.Clear().Append(l[3..].Trim());
				}
				else if (current.Length > 0 && l.Length > 0)
					current.Append(' ').Append(l);
			}

			if (current.Length > 0)
				objectives.Add(mo.Add(player, current.ToString(), "Primary", inhibitAnnouncement: true));

			if (objectives.Count == 0)
				objectives.Add(mo.Add(player, "Complete the mission.", "Primary", inhibitAnnouncement: true));
		}

		void LoadTrees(Map map)
		{
			var difficulty = world.LobbyInfo.GlobalSettings.OptionOrDefault(info.DifficultyOption, "normal");
			var level = difficulty switch { "easy" => 0, "hard" => 2, _ => 1 };
			var present = scenario.Placements.Where(p => !p.IsThing).Select(p => p.Team).ToHashSet();

			foreach (var t in scenario.Teams.Values.OrderBy(t => t.Index))
			{
				if (!teams.ContainsKey(t.Index))
					continue;

				// A team with nothing on the map and no orders of its own takes no part.
				if (!present.Contains(t.Index) && t.EndFile == null && t.FsmFiles.Length == 0)
					continue;

				var end = t.EndFile != null ? Load(map, t.EndFile, t.Index) : null;
				trees.Add(end ?? DrConditionTree.KillAll(t.Index));

				if (t.Index == 0)
					continue;

				var fsmName = t.FsmFiles.Length > 0
					? t.FsmFiles[Math.Min(level, t.FsmFiles.Length - 1)]
					: present.Contains(t.Index) ? $"def_{t.Side:00}_{level}.fsm" : null;

				if (fsmName != null)
				{
					var fsm = Load(map, fsmName, t.Index);
					if (fsm != null)
						trees.Add(fsm);
				}
			}
		}

		static DrConditionTree Load(Map map, string file, int team)
		{
			var name = file.ToLowerInvariant();
			if (!map.Package.Contains(name))
			{
				Log.Write("debug", $"DrScenarioScript: missing {name} for team {team}");
				return null;
			}

			using (var s = map.Open(name))
			{
				var root = DrScript.Parse(s).FirstOrDefault(n => n.Is("DefineEndCondTree") || n.Is("DefineAICondTree"));
				return root == null ? null : new DrConditionTree(team, name, root);
			}
		}

		void OnActorAdded(Actor a)
		{
			if (!started || a.Owner == null)
				return;

			var team = TeamOf(a.Owner);
			if (!teams.ContainsKey(team) || teams[team] != a.Owner)
				return;

			var key = (team, a.Info.Name);
			built[key] = built.GetValueOrDefault(key) + 1;
		}

		void ITick.Tick(Actor self)
		{
			if (!started)
			{
				started = true;
				Start();
			}

			ticks++;
			TickPatrols();

			if (finished || ticks % info.EvaluateInterval != 0)
				return;

			foreach (var tree in trees)
			{
				tree.Tick(this);
				if (tree.IsEndTree && tree.Won)
				{
					EndGame(tree.Team);
					return;
				}

				if (tree.IsEndTree && tree.Team == 0 && tree.Halted && trees.Count(t => t.IsEndTree && t.Team == 0) == 1 && tree.TimeLimit > 0 && Cycle > tree.TimeLimit)
				{
					// Out of time: the player can never win now.
					EndGame(-1);
					return;
				}
			}
		}

		void EndGame(int winner)
		{
			finished = true;
			Log.Write("drscenario", $"{Cycle,7} game over, winner team {winner}");
			if (!teams.TryGetValue(0, out var player))
				return;

			var mo = player.PlayerActor.TraitOrDefault<MissionObjectives>();
			var won = winner == 0 || (winner > 0 && Alliance(0, winner) == 2 && Alliance(winner, 0) == 2);
			if (won)
			{
				foreach (var key in new[] { 2, 3 })
					if (briefing.TryGetValue(key, out var text) && !string.IsNullOrEmpty(text))
						TextNotificationsManager.AddMissionLine("Togra", text.Replace("\\n", " ").Replace("  ", " "), Color.White);

				foreach (var o in objectives)
					mo?.MarkCompleted(player, o);
			}
			else if (objectives.Count > 0)
				mo?.MarkFailed(player, objectives[0]);
		}

		int Alliance(int team, int other) => scenario.Teams.TryGetValue(team, out var t) && other < t.Alliance.Length ? t.Alliance[other] : 0;

		void TickPatrols()
		{
			if (patrols.Count == 0 || ticks % 25 != 0)
				return;

			foreach (var a in patrols.Keys.ToList())
			{
				if (a.IsDead || !a.IsInWorld || specialForces.Contains(a) && !a.IsIdle)
				{
					if (a.IsDead)
						patrols.Remove(a);
					continue;
				}

				if (!a.IsIdle)
					continue;

				var (patrol, next, step) = patrols[a];
				var (x, y) = patrol.Points[next];
				a.QueueActivity(new AttackMoveActivity(a, () => a.Trait<IMove>().MoveTo(DrScenario.TileToCell(x, y), 2)));

				var count = patrol.Points.Count;
				if (patrol.Loop)
					next = (next + 1) % count;
				else
				{
					if (next + step >= count || next + step < 0)
						step = -step;
					next = Math.Clamp(next + step, 0, count - 1);
				}

				patrols[a] = (patrol, next, step);
			}
		}

		// Criteria

		bool IDrScenarioContext.Evaluate(DrCriterion c, int team)
		{
			var n = c.Node;
			var player = teams.GetValueOrDefault(team);
			switch (c.Name)
			{
				case "crittimergame":
					return Cycle >= n.IntArg(0);

				case "critdestroyunit":
				case "critdestroybuilding":
				case "critdestroything":
					return n.Ids.All(id => !actorsById.TryGetValue(id, out var a) || a.IsDead || !a.IsInWorld);

				case "critenemyinregion":
					return player != null && UnitsInRegion(n.IntArg(0)).Any(a => player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy);

				case "critinregion":
					return player != null && UnitsInRegion(n.IntArg(0)).Any(a => a.Owner == player);

				case "critteaminregion":
					return teams.TryGetValue(n.IntArg(1), out var other) && UnitsInRegion(n.IntArg(0)).Any(a => a.Owner == other);

				case "critholdregion":
				{
					var inRegion = UnitsInRegion(n.IntArg(0)).ToList();
					return player != null && inRegion.Any(a => player.IsAlliedWith(a.Owner))
						&& !inRegion.Any(a => player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy);
				}

				case "critharassregion":
					// Approximated: this team's units are fighting in the region.
					return player != null && UnitsInRegion(n.IntArg(0)).Any(a => a.Owner == player && !a.IsIdle);

				case "critmoveunitstoregion":
				{
					var visit = (VisitCriterion)c;
					var region = RegionBox(n.IntArg(0));
					foreach (var id in n.Ids)
						if (actorsById.TryGetValue(id, out var a) && !a.IsDead && a.IsInWorld && region.Contains(a.CenterPosition))
							visit.Visited.Add(id);
					return false;
				}

				case "crithavecredits":
					return player != null && Cash(player) >= n.IntArg(0);

				case "critbuildbuilding":
					return player != null && CountBuildings(player, n.Arg(0), n.IntArg(1), false) >= Math.Max(1, n.IntArg(2));

				case "critbeginbuildbuilding":
					return player != null && CountBuildings(player, n.Arg(0), n.IntArg(1), true) >= Math.Max(1, n.IntArg(2));

				case "critbuildunit":
				{
					var type = ActorType(n.Arg(0), false);
					return type != null && built.GetValueOrDefault((team, type)) >= Math.Max(1, n.IntArg(1));
				}

				case "critkillteamunits":
				{
					var target = n.IntArg(0);
					return teams.TryGetValue(target, out var tp) && Units(tp).Count() * 100 <= startUnits.GetValueOrDefault(target) * n.IntArg(1);
				}

				case "critdestroyteambuildings":
				{
					var target = n.IntArg(0);
					return teams.TryGetValue(target, out var tp) && Buildings(tp).Count() * 100 <= startBuildings.GetValueOrDefault(target) * n.IntArg(1);
				}

				case "critkillenemyunits":
				{
					var enemies = EnemyTeams(team).ToList();
					return Units(enemies).Count() * 100 <= enemies.Sum(t => startUnits.GetValueOrDefault(TeamOf(t))) * n.IntArg(0);
				}

				case "critdestroyenemybuildings":
				{
					var enemies = EnemyTeams(team).ToList();
					var start = enemies.Sum(t => startBuildings.GetValueOrDefault(TeamOf(t)));
					return (start - Buildings(enemies).Count()) * 100 >= start * n.IntArg(0);
				}

				case "critkillall":
				case "critkillallandallies":
				{
					// Civilians and the map's own walls and wells never count against a win.
					var rivals = teams.Where(kv => kv.Key != team && kv.Key != 8 && SideOf(kv.Key) != 2
						&& (c.Name == "critkillallandallies" || Alliance(team, kv.Key) != 2)).Select(kv => kv.Value).ToList();
					return !Units(rivals).Any() && !Buildings(rivals).Any();
				}

				case "critdestroybuildingtype":
				{
					var type = ActorType(n.Arg(0), true);
					return teams.TryGetValue(n.IntArg(1), out var tp) && !Buildings(tp).Any(a => SameBuilding(a, type, true));
				}

				case "critkillunittype":
				{
					var type = ActorType(n.Arg(0), false);
					return teams.TryGetValue(n.IntArg(1), out var tp) && !Units(tp).Any(a => a.Info.Name == type);
				}

				case "critmoreunitsthanenemy":
				case "critlessunitsthanenemy":
				{
					var enemies = EnemyTeams(team).ToList();
					var max = enemies.Count == 0 ? 0 : enemies.Max(e => Units(e).Count());
					var own = player == null ? 0 : Units(player).Count();
					if (c.Name == "critmoreunitsthanenemy")
						return enemies.Count == 0 || own * 100 > max * n.IntArg(0);
					return enemies.Count > 0 && own * 100 < max * n.IntArg(0);
				}

				case "critcollectwater":
				case "critcollectmineral":
					// Resources sell straight to credits here, so earnings stand in for what was mined.
					return player != null && player.PlayerActor.Trait<PlayerResources>().Earned >= n.IntArg(0);

				case "critstealplan":
					// Needs the Infiltrator's plan stealing, which OpenDR does not have yet.
					return false;

				default:
					Log.Write("debug", $"DrScenarioScript: unsupported criterion {n}");
					return false;
			}
		}

		// Actions

		void IDrScenarioContext.Act(DrScriptNode n, int team)
		{
			Log.Write("drscenario", $"{Cycle,7} team {team}: {n}");
			switch (n.Name.ToLowerInvariant())
			{
				case "triggerspecialforces":
					TriggerSpecialForces(n.IntArg(0), n.IntArg(1));
					break;

				case "releasespecialforces":
					if (scenario.SpecialForces.TryGetValue(n.IntArg(0), out var released))
						foreach (var id in released.UnitIds)
							if (actorsById.TryGetValue(id, out var a))
								specialForces.Remove(a);
					break;

				case "givespecialforces":
					if (scenario.SpecialForces.TryGetValue(n.IntArg(0), out var given) && teams.TryGetValue(n.IntArg(1), out var to))
						foreach (var id in given.UnitIds)
							if (actorsById.TryGetValue(id, out var a) && !a.IsDead && a.IsInWorld)
								a.ChangeOwner(to);
					break;

				case "adjustregionpri":
				{
					if (!RegionPriorities.TryGetValue(team, out var pri))
						RegionPriorities[team] = pri = new();
					pri[n.IntArg(0)] = (n.IntArg(1), n.IntArg(2), n.IntArg(3));
					break;
				}

				case "bonuscredits":
					if (teams.TryGetValue(n.IntArg(0), out var rich))
						rich.PlayerActor.Trait<PlayerResources>().GiveCash(n.IntArg(1));
					break;

				case "setaipfile":
					AipChanged(team, n.Arg(0)?.ToLowerInvariant());
					break;

				case "triggermessage":
				{
					var key = n.Arg(0);
					if (key != null && messages.TryGetValue(key, out var text))
						TextNotificationsManager.AddMissionLine("Mission", text, Color.White);
					break;
				}

				case "setalliance":
					SetAlliance(team, n);
					break;

				case "setmessagefile":
				case "triggerevent":
					break;

				default:
					Log.Write("debug", $"DrScenarioScript: unsupported action {n}");
					break;
			}
		}

		void TriggerSpecialForces(int id, int regionId)
		{
			if (!scenario.SpecialForces.TryGetValue(id, out var sf) || !scenario.Regions.TryGetValue(regionId, out var region))
				return;

			var tl = DrScenario.PixelToCell(region.X1, region.Y1);
			var br = DrScenario.PixelToCell(region.X2, region.Y2);
			foreach (var unitId in sf.UnitIds)
			{
				if (!actorsById.TryGetValue(unitId, out var a) || a.IsDead || !a.IsInWorld)
					continue;

				var move = a.TraitOrDefault<IMove>();
				if (move == null)
					continue;

				// Spread through the region, as the original did; this overrides any earlier orders.
				var cell = new CPos(world.SharedRandom.Next(tl.X, br.X + 1), world.SharedRandom.Next(tl.Y, br.Y + 1));
				patrols.Remove(a);
				a.CancelActivity();
				a.QueueActivity(new AttackMoveActivity(a, () => move.MoveTo(cell, 2)));
			}
		}

		void SetAlliance(int team, DrScriptNode n)
		{
			if (!teams.TryGetValue(team, out var p) || !scenario.Teams.TryGetValue(team, out var t))
				return;

			for (var i = 0; i < n.Args.Length && i < 8; i++)
			{
				t.Alliance[i] = n.IntArg(i);
				if (i == team || !teams.TryGetValue(i, out var q))
					continue;

				p.AlliedPlayersMask = p.AlliedPlayersMask.Except(q.PlayerMask);
				p.EnemyPlayersMask = p.EnemyPlayersMask.Except(q.PlayerMask);
				if (t.Alliance[i] == 2)
					p.AlliedPlayersMask = p.AlliedPlayersMask.Union(q.PlayerMask);
				else if (t.Alliance[i] == 0)
					p.EnemyPlayersMask = p.EnemyPlayersMask.Union(q.PlayerMask);
			}
		}

		// Queries

		int SideOf(int team) => scenario.Teams.TryGetValue(team, out var t) ? t.Side : 2;

		IEnumerable<Player> EnemyTeams(int team)
		{
			return teams.Where(kv => kv.Key != team && Alliance(team, kv.Key) == 0 && kv.Key != 8).Select(kv => kv.Value);
		}

		static int Cash(Player p)
		{
			var r = p.PlayerActor.Trait<PlayerResources>();
			return r.Cash + r.Resources;
		}

		IEnumerable<Actor> Owned(Player p) => world.Actors.Where(a => a.Owner == p && !a.IsDead && a.IsInWorld);

		static bool IsUnit(Actor a) => a.Info.HasTraitInfo<MobileInfo>() || a.Info.HasTraitInfo<AircraftInfo>();

		static bool IsBuilding(Actor a) => a.Info.HasTraitInfo<BuildingInfo>();

		IEnumerable<Actor> Units(Player p) => Owned(p).Where(IsUnit);

		IEnumerable<Actor> Buildings(Player p) => Owned(p).Where(IsBuilding);

		IEnumerable<Actor> Units(ICollection<Player> ps) => world.Actors.Where(a => ps.Contains(a.Owner) && !a.IsDead && a.IsInWorld && IsUnit(a));

		IEnumerable<Actor> Buildings(ICollection<Player> ps) => world.Actors.Where(a => ps.Contains(a.Owner) && !a.IsDead && a.IsInWorld && IsBuilding(a));

		/// <summary>A region in world coordinates, from its pixels (24 to a cell) and the map's one-cell border.</summary>
		WorldBox RegionBox(int id)
		{
			if (!scenario.Regions.TryGetValue(id, out var r))
				return default;

			static int W(int px) => px * 1024 / DrScenario.PixelsPerTile + 1024;
			return new WorldBox(new WPos(W(r.X1), W(r.Y1), 0), new WPos(W(r.X2), W(r.Y2), 0));
		}

		IEnumerable<Actor> UnitsInRegion(int id)
		{
			var box = RegionBox(id);
			if (!box.Valid)
				return Enumerable.Empty<Actor>();

			return world.ActorMap.ActorsInBox(box.TopLeft, box.BottomRight).Where(a => !a.IsDead && a.IsInWorld && IsUnit(a));
		}

		/// <summary>The actor type for an original unit or building name.</summary>
		static string ActorType(string drType, bool building)
		{
			if (drType == null)
				return null;

			var table = building ? ImportDrMapCommand.BuildingNames : ImportDrMapCommand.UnitNames;
			var match = table.FirstOrDefault(kv => kv.Key.Equals(drType, StringComparison.OrdinalIgnoreCase)).Value;
			return match?.ToLowerInvariant();
		}

		static bool SameBuilding(Actor a, string type, bool includeConstructing)
		{
			if (type == null)
				return false;

			var name = a.Info.Name;
			return name == type || (includeConstructing && name == type + ".constructing");
		}

		int CountBuildings(Player p, string drType, int regionId, bool includeConstructing)
		{
			var type = ActorType(drType, true);
			var box = regionId == 0 ? default : RegionBox(regionId);
			return Owned(p).Count(a => SameBuilding(a, type, includeConstructing) && (regionId == 0 || box.Contains(a.CenterPosition)));
		}

		readonly struct WorldBox
		{
			public readonly WPos TopLeft;
			public readonly WPos BottomRight;
			public readonly bool Valid;

			public WorldBox(WPos tl, WPos br)
			{
				TopLeft = tl;
				BottomRight = br;
				Valid = true;
			}

			public bool Contains(WPos p) => Valid && p.X >= TopLeft.X && p.X <= BottomRight.X && p.Y >= TopLeft.Y && p.Y <= BottomRight.Y;
		}
	}
}
