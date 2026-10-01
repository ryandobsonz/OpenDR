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
using System.Linq;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Dr.FileFormats;
using OpenRA.Mods.Dr.UtilityCommands;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Plays a campaign team from its mission's AIP files, switched by its FSM through DrScenarioScript.",
		"Builds as the AIP's accounts say (the Building and Unit Construction System) and sends troops where its",
		"priorities say (the Troop Allocation System), both as the original AIP manual describes.")]
	public class DrAipBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between construction decisions.")]
		public readonly int BuildInterval = 25;

		[Desc("Fewest ticks between troop allocations, whatever the AIP's recompute_strategy_period.")]
		public readonly int MinimumStrategyInterval = 75;

		[Desc("Cells a side of each strategic goal on the coarse grid.")]
		public readonly int GridSize = 8;

		[Desc("Unit strength is its cost divided by this, standing in for the original's firepower and hitpoints formula.")]
		public readonly int StrengthDivisor = 10;

		[Desc("Units that never join a squad.")]
		public readonly HashSet<string> NonCombatTypes = new() { "constructionrig", "freighter", "hoverfreighter" };

		[Desc("Cells from the base centre a new building may go.")]
		public readonly int MaxBaseRadius = 14;

		public override object Create(ActorInitializer init) { return new DrAipBotModule(init.Self, this); }
	}

	public class DrAipBotModule : ConditionalTrait<DrAipBotModuleInfo>, IBotTick
	{
		readonly World world;
		readonly Player player;
		DrScenarioScript scenario;
		int team = -1;

		string aipName;
		AipFile aip;
		List<AccountState> accounts = new();
		int lastCash;
		int buildCountdown;
		int strategyCountdown;
		readonly Dictionary<Actor, Goal> assignments = new();
		readonly HashSet<CPos> unreachable = new();

		public DrAipBotModule(Actor self, DrAipBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		class AccountState
		{
			public AipAccount Account;
			public int Balance;
			public readonly Dictionary<AipAccountElement, int> Built = new();
		}

		class Goal
		{
			public CPos Cell;
			public int Threat;
			public int OwnBuildings;
			public int EnemyBuildings;
			public int Scripted;
			public int ScriptedMin;
			public int ScriptedMax;
			public bool Explored;
			public int Assigned;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (scenario == null)
			{
				scenario = world.WorldActor.TraitOrDefault<DrScenarioScript>();
				if (scenario == null || scenario.Scenario == null)
				{
					scenario = null;
					return;
				}

				team = scenario.TeamOf(player);
			}

			var current = scenario.CurrentAip(team);
			if (current != aipName)
				LoadAip(current);

			if (aip == null)
				return;

			if (--buildCountdown <= 0)
			{
				buildCountdown = Info.BuildInterval;
				Construct(bot);
			}

			if (--strategyCountdown <= 0)
			{
				strategyCountdown = Math.Max(Info.MinimumStrategyInterval, aip.Int("recompute_strategy_period", 100));
				AllocateTroops(bot);
			}
		}

		void LoadAip(string name)
		{
			aipName = name;
			aip = null;
			if (name == null || !world.Map.Package.Contains(name))
				return;

			using (var s = world.Map.Open(name))
				aip = new AipFile(s);

			// The team's money is split between the new accounts by budget; UNLIMITED accounts draw on it directly.
			var cash = Cash();
			var shares = aip.Accounts.Where(a => a.Budget > 0).Sum(a => a.Budget);
			accounts = aip.Accounts.Select(a => new AccountState
			{
				Account = a,
				Balance = a.Budget > 0 && shares > 0 ? Capped(a, cash * a.Budget / shares) : 0
			}).ToList();

			lastCash = cash;
			scenario.Trace($"team {team} AIP {name}: " + string.Join(", ", accounts.Select(a => $"{a.Account.Name} {a.Balance}")));
			buildCountdown = 0;
			strategyCountdown = 0;
		}

		int Cash()
		{
			var r = player.PlayerActor.Trait<PlayerResources>();
			return r.Cash + r.Resources;
		}

		static int Capped(AipAccount a, int balance) => a.Cap >= 0 ? Math.Min(balance, a.Cap) : balance;

		// The Building and Unit Construction System

		void Construct(IBot bot)
		{
			var cash = Cash();
			var income = cash - lastCash;
			var shares = accounts.Where(a => a.Account.Budget > 0).Sum(a => a.Account.Budget);
			if (income > 0 && shares > 0)
				foreach (var a in accounts.Where(a => a.Account.Budget > 0))
					a.Balance = Capped(a.Account, a.Balance + income * a.Account.Budget / shares);

			// Accounts sharing a facility take turns weighted by the priority they are building at.
			var order = accounts.Select(a => (Account: a, Level: CurrentLevel(a)))
				.Where(x => x.Level != null)
				.OrderByDescending(x => world.LocalRandom.Next(Math.Max(1, x.Level.Value)))
				.ToList();

			var queues = AIUtils.FindQueuesByCategory(player).SelectMany(g => g).ToList();
			foreach (var (account, level) in order)
				BuildAtLevel(bot, account, level.Value, queues);

			lastCash = Cash();
		}

		/// <summary>The highest priority with something still to build; RATIO elements are never done.</summary>
		int? CurrentLevel(AccountState a)
		{
			foreach (var group in a.Account.Elements.GroupBy(e => e.Priority).OrderByDescending(g => g.Key))
				if (group.Any(e => !Satisfied(a, e)))
					return group.Key;

			return null;
		}

		bool Satisfied(AccountState a, AipAccountElement e)
		{
			return e.BuildType switch
			{
				AipBuildType.NumberToHave => Have(e.Item) >= e.Amount,
				AipBuildType.NumberToBuild => a.Built.GetValueOrDefault(e) >= e.Amount,
				_ => false,
			};
		}

		void BuildAtLevel(IBot bot, AccountState a, int level, List<ProductionQueue> queues)
		{
			var group = a.Account.Elements.Where(e => e.Priority == level).ToList();
			IEnumerable<AipAccountElement> wanted;
			if (group.Any(e => e.BuildType is AipBuildType.RatioToBuild or AipBuildType.RatioToHave))
			{
				// Build whichever element is furthest behind its share of the ratio.
				var byShortfall = group.Where(e => e.Amount > 0)
					.OrderBy(e => (e.BuildType == AipBuildType.RatioToHave ? Have(e.Item) : a.Built.GetValueOrDefault(e)) / (double)e.Amount);
				wanted = byShortfall.Take(1);
			}
			else
				wanted = group.Where(e => !Satisfied(a, e));

			foreach (var e in wanted)
			{
				var info = ActorFor(e.Item, out var isBuilding);
				if (info == null)
					continue;

				var cost = info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
				var unlimited = a.Account.Budget < 0;
				if (unlimited ? Cash() < cost : a.Balance < cost)
					continue;

				var started = isBuilding ? StartBuilding(bot, info) : StartUnit(bot, info, queues);
				if (!started)
					continue;

				if (!unlimited)
					a.Balance -= cost;

				scenario.Trace($"team {team} {a.Account.Name} builds {info.Name}");

				a.Built[e] = a.Built.GetValueOrDefault(e) + 1;
			}
		}

		/// <summary>The actor for an original item name; buildings are made through their .constructing form.</summary>
		ActorInfo ActorFor(string item, out bool isBuilding)
		{
			isBuilding = false;
			var rules = world.Map.Rules.Actors;
			var unit = ImportDrMapCommand.UnitNames.FirstOrDefault(kv => kv.Key.Equals(item, StringComparison.OrdinalIgnoreCase)).Value;
			if (unit != null)
				return rules.GetValueOrDefault(unit.ToLowerInvariant());

			var building = ImportDrMapCommand.BuildingNames.FirstOrDefault(kv => kv.Key.Equals(item, StringComparison.OrdinalIgnoreCase)).Value;
			if (building == null)
				return null;

			isBuilding = true;
			return rules.GetValueOrDefault(building.ToLowerInvariant() + ".constructing");
		}

		/// <summary>On the map, being built, or queued.</summary>
		int Have(string item)
		{
			var info = ActorFor(item, out var isBuilding);
			if (info == null)
				return 0;

			var name = info.Name;
			var finished = isBuilding ? name[..^".constructing".Length] : name;
			var count = world.Actors.Count(a => a.Owner == player && !a.IsDead && (a.Info.Name == name || a.Info.Name == finished));
			count += AIUtils.FindQueuesByCategory(player).SelectMany(g => g).Sum(q => q.AllQueued().Count(i => i.Item == name));
			if (isBuilding)
				count += world.Actors.Count(a => a.Owner == player && !a.IsDead && a.CurrentActivity != null && rigTargets.TryGetValue(a, out var t) && t == name);

			return count;
		}

		readonly Dictionary<Actor, string> rigTargets = new();

		bool StartUnit(IBot bot, ActorInfo info, List<ProductionQueue> queues)
		{
			var queue = queues.Where(q => q.Enabled && q.CanBuild(info) && !q.AllQueued().Any())
				.MinByOrDefault(q => q.AllQueued().Count());
			if (queue == null)
				return false;

			bot.QueueOrder(Order.StartProduction(queue.Actor, info.Name, 1));
			return true;
		}

		bool StartBuilding(IBot bot, ActorInfo info)
		{
			foreach (var kv in rigTargets.Where(kv => kv.Key.IsDead || kv.Key.IsIdle).ToList())
				rigTargets.Remove(kv.Key);

			var rig = world.ActorsHavingTrait<BuilderUnit>()
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld && !rigTargets.ContainsKey(a)
					&& a.TraitsImplementing<BuilderUnit>().Any(b => b.Enabled && b.CanBuild(info)))
				.FirstOrDefault();
			if (rig == null)
				return false;

			var location = BuildLocation(info);
			if (location == null)
				return false;

			bot.QueueOrder(new Order("BuildUnitPlaceBuilding", player.PlayerActor, Target.FromCell(world, location.Value), false)
			{
				TargetString = info.Name,
				ExtraLocation = location.Value,
				ExtraData = rig.ActorID,
				SuppressVisualFeedback = true
			});

			rigTargets[rig] = info.Name;
			return true;
		}

		CPos BaseCenter()
		{
			var buildings = world.ActorsHavingTrait<Building>().Where(a => a.Owner == player && !a.IsDead).ToList();
			var hq = buildings.FirstOrDefault(a => a.Info.Name.StartsWith("hq.", StringComparison.Ordinal));
			if (hq != null)
				return hq.Location;

			if (buildings.Count > 0)
				return buildings[0].Location;

			var rig = world.ActorsHavingTrait<BuilderUnit>().FirstOrDefault(a => a.Owner == player && !a.IsDead);
			return rig?.Location ?? player.HomeLocation;
		}

		CPos? BuildLocation(ActorInfo info)
		{
			var bi = info.TraitInfoOrDefault<BuildingInfo>();
			if (bi == null)
				return null;

			var center = BaseCenter();
			foreach (var cell in world.Map.FindTilesInAnnulus(center, 2, Info.MaxBaseRadius).Shuffle(world.LocalRandom))
				if (world.CanPlaceBuilding(cell, info, bi, null) && bi.IsCloseEnoughToBase(world, player, info, cell))
					return cell;

			return null;
		}

		// The Troop Allocation System

		int Strength(Actor a) => Math.Max(1, (a.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 100) / Info.StrengthDivisor);

		static bool IsUnit(Actor a) => a.Info.HasTraitInfo<MobileInfo>() || a.Info.HasTraitInfo<AircraftInfo>();

		void AllocateTroops(IBot bot)
		{
			var g = Info.GridSize;
			var map = world.Map;
			var cols = (map.MapSize.Width + g - 1) / g;
			var rows = (map.MapSize.Height + g - 1) / g;
			var goals = new Goal[cols, rows];
			for (var x = 0; x < cols; x++)
				for (var y = 0; y < rows; y++)
					goals[x, y] = new Goal { Cell = map.Clamp(new CPos(x * g + g / 2, y * g + g / 2)) };

			Goal GoalAt(CPos c) => goals[Math.Clamp(c.X / g, 0, cols - 1), Math.Clamp(c.Y / g, 0, rows - 1)];

			var threat = new double[cols, rows];
			foreach (var a in world.Actors)
			{
				if (a.IsDead || !a.IsInWorld || a.OccupiesSpace == null || player.RelationshipWith(a.Owner) != PlayerRelationship.Enemy)
					continue;

				var gx = Math.Clamp(a.Location.X / g, 0, cols - 1);
				var gy = Math.Clamp(a.Location.Y / g, 0, rows - 1);
				if (IsUnit(a))
				{
					if (a.CanBeViewedByPlayer(player))
						threat[gx, gy] += Strength(a);
				}
				else if (a.Info.HasTraitInfo<BuildingInfo>() && player.Shroud.IsExplored(a.Location))
					goals[gx, gy].EnemyBuildings++;
			}

			foreach (var a in world.ActorsHavingTrait<Building>().Where(a => a.Owner == player && !a.IsDead))
				GoalAt(a.Location).OwnBuildings++;

			// Threat bleeds into neighbouring cells, so a quiet cell beside a crowded one is not thought safe.
			var cycles = aip.Int("relaxation_cycles", 1);
			var coefficient = aip.Double("relaxation_coefficient", 0.5);
			for (var c = 0; c < cycles; c++)
			{
				var next = (double[,])threat.Clone();
				for (var x = 0; x < cols; x++)
				{
					for (var y = 0; y < rows; y++)
					{
						var bleed = 0.0;
						if (x > 0) bleed += threat[x - 1, y];
						if (y > 0) bleed += threat[x, y - 1];
						if (x < cols - 1) bleed += threat[x + 1, y];
						if (y < rows - 1) bleed += threat[x, y + 1];
						next[x, y] += bleed * coefficient;
					}
				}

				threat = next;
			}

			for (var x = 0; x < cols; x++)
			{
				for (var y = 0; y < rows; y++)
				{
					goals[x, y].Threat = (int)threat[x, y];
					goals[x, y].Explored = player.Shroud.IsExplored(goals[x, y].Cell);
				}
			}

			// Scripted priorities from the FSM's AdjustRegionPri.
			if (scenario.RegionPriorities.TryGetValue(team, out var priorities))
			{
				foreach (var (regionId, pri) in priorities)
				{
					if (!scenario.Scenario.Regions.TryGetValue(regionId, out var r))
						continue;

					var tl = DrScenario.PixelToCell(r.X1, r.Y1);
					var br = DrScenario.PixelToCell(r.X2, r.Y2);
					for (var x = tl.X / g; x <= br.X / g && x < cols; x++)
					{
						for (var y = tl.Y / g; y <= br.Y / g && y < rows; y++)
						{
							goals[x, y].Scripted = pri.Priority;
							goals[x, y].ScriptedMin = pri.Min;
							goals[x, y].ScriptedMax = pri.Max;
						}
					}
				}
			}

			var units = world.Actors.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld && IsUnit(a)
				&& !Info.NonCombatTypes.Contains(a.Info.Name) && a.Info.HasTraitInfo<AttackBaseInfo>()
				&& !scenario.IsSpecialForce(a)).ToList();

			foreach (var a in assignments.Keys.Where(a => a.IsDead || !units.Contains(a)).ToList())
				assignments.Remove(a);

			if (units.Count == 0)
				return;

			var threatPriority = aip.Int("threat_priority", 1);
			var distancePriority = aip.Int("distance_priority", -3);
			var defendPriority = aip.Int("defend_buildings_priority", 0);
			var attackPriority = aip.Int("attack_enemy_base_priority", 0);
			var persistencePriority = aip.Int("persistence_priority", 0);
			var explorationPriority = aip.Int("exploration_priority", 0);
			var scriptedPriority = aip.Int("scripted_priority", 0);
			var minRatio = aip.Double("min_matching_force_ratio", 1.0);
			var maxRatio = aip.Double("max_matching_force_ratio", 2.0);
			var minDefense = aip.Int("min_building_defense_force", 0);
			var maxDefense = aip.Int("max_building_defense_force", 0);
			var minExplore = aip.Int("min_exploration_force", 0);
			var maxExplore = aip.Int("max_exploration_force", 0);

			// What each goal needs, in strength: the smallest force worth sending, and the most worth sending.
			var needs = new Dictionary<Goal, (int Min, int Max)>();
			foreach (var goal in goals)
			{
				goal.Assigned = 0;
				int min = 0, max = 0;
				if (goal.Threat > 0)
				{
					min = (int)(goal.Threat * minRatio);
					max = (int)(goal.Threat * maxRatio);
				}

				if (goal.OwnBuildings > 0 && goal.Threat > 0)
				{
					min = Math.Max(min, minDefense);
					max = Math.Max(max, maxDefense);
				}

				if (goal.EnemyBuildings > 0 && attackPriority > 0)
				{
					min = Math.Max(min, Math.Max(1, (int)(goal.Threat * minRatio)));
					max = Math.Max(max, Math.Max(min, maxDefense));
				}

				if (goal.Scripted > 0)
				{
					min = Math.Max(min, goal.ScriptedMin);
					max = Math.Max(max, goal.ScriptedMax);
				}

				if (!goal.Explored && explorationPriority > 0)
				{
					min = Math.Max(min, minExplore);
					max = Math.Max(max, maxExplore);
				}

				if (max > 0 && !unreachable.Contains(goal.Cell))
					needs[goal] = (Math.Max(1, min), Math.Max(min, max));
			}

			if (needs.Count == 0)
				return;

			// Squads: units already on a goal stay together; the rest group by the cell they stand in.
			var squads = units.GroupBy(a => assignments.TryGetValue(a, out var goal) ? goal.Cell : GoalAt(a.Location).Cell).ToList();

			var matchings = new List<(double Value, IGrouping<CPos, Actor> Squad, Goal Goal)>();
			foreach (var squad in squads)
			{
				var center = squad.First().Location;
				foreach (var goal in needs.Keys)
				{
					var distance = (goal.Cell - center).Length;
					var persistent = squad.Any(a => assignments.TryGetValue(a, out var old) && old.Cell == goal.Cell);
					double value;
					if (goal.Explored)
						value = threatPriority * goal.Threat + distancePriority * distance + defendPriority * goal.OwnBuildings
							+ attackPriority * goal.EnemyBuildings + scriptedPriority * goal.Scripted;
					else
						value = distancePriority * distance + scriptedPriority * goal.Scripted + explorationPriority;

					if (persistent)
						value += persistencePriority;

					matchings.Add((value, squad, goal));
				}
			}

			// Commit troops to the best matchings until each goal has enough, then drop goals that cannot get their minimum.
			var allocation = new Dictionary<Actor, Goal>();
			foreach (var (_, squad, goal) in matchings.OrderByDescending(m => m.Value))
			{
				var (_, max) = needs[goal];
				foreach (var a in squad.OrderBy(a => (a.Location - goal.Cell).LengthSquared))
				{
					if (goal.Assigned >= max)
						break;

					if (allocation.ContainsKey(a))
						continue;

					allocation[a] = goal;
					goal.Assigned += Strength(a);
				}
			}

			foreach (var (a, goal) in allocation.ToList())
				if (goal.Assigned < needs[goal].Min)
					allocation.Remove(a);

			// Send each new or idle group on its way, to the nearest cell of the goal it can reach.
			var pathFinder = world.WorldActor.Trait<IPathFinder>();
			foreach (var group in allocation.GroupBy(kv => kv.Value))
			{
				var goal = group.Key;
				var moving = group.Select(kv => kv.Key)
					.Where(a => !assignments.TryGetValue(a, out var old) || old.Cell != goal.Cell
						|| (a.IsIdle && (a.Location - goal.Cell).LengthSquared > (Info.GridSize / 2 + 2) * (Info.GridSize / 2 + 2)))
					.ToArray();

				foreach (var a in group.Select(kv => kv.Key))
					assignments[a] = goal;

				if (moving.Length == 0)
					continue;

				var target = goal.Cell;
				var leader = moving.FirstOrDefault(a => a.TraitOrDefault<Mobile>() != null);
				if (leader != null)
				{
					var locomotor = leader.Trait<Mobile>().Locomotor;
					var reachable = map.FindTilesInCircle(goal.Cell, Info.GridSize / 2 + 1)
						.Where(c => pathFinder.PathExistsForLocomotor(locomotor, leader.Location, c))
						.Select(c => (CPos?)c).FirstOrDefault();
					if (reachable == null)
					{
						// Nothing there can be walked to: stop considering it, and free the squad.
						unreachable.Add(goal.Cell);
						foreach (var a in group.Select(kv => kv.Key))
							assignments.Remove(a);
						continue;
					}

					target = reachable.Value;
				}

				scenario.Trace($"team {team} sends {moving.Length} to {target} (threat {goal.Threat}, enemy buildings {goal.EnemyBuildings}, scripted {goal.Scripted})");
				bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, target), false, groupedActors: moving));
			}
		}
	}
}
