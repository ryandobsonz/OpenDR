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
using OpenRA.Activities;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	/// <summary>The original's unit orders (IGI_ORORD1-3; SetSOrderAutoMove 0-2), carried out until the player gives another.</summary>
	public enum DrUnitOrder { None, Scout, Harass, SearchAndDestroy }

	[TraitLocation(SystemActors.Player)]
	[Desc("A player's default behaviour for new units: GENERAL.TXT's SetTactAI(0 2 1), changed by the ORDERS tab's Use As Default.")]
	public class DrTacticsDefaultsInfo : TraitInfo
	{
		[Desc("Pursuit range, damage tolerance and independence: 0 low, 1 medium, 2 high.")]
		public readonly int Pursuit = 0;
		public readonly int Tolerance = 2;
		public readonly int Independence = 1;

		public override object Create(ActorInitializer init) { return new DrTacticsDefaults(this); }
	}

	public class DrTacticsDefaults : IResolveOrder
	{
		public const string OrderName = "DrSetDefaultTactics";

		public int Pursuit, Tolerance, Independence;

		public DrTacticsDefaults(DrTacticsDefaultsInfo info)
		{
			Pursuit = info.Pursuit;
			Tolerance = info.Tolerance;
			Independence = info.Independence;
		}

		public static uint Pack(int pursuit, int tolerance, int independence) => (uint)(pursuit | tolerance << 2 | independence << 4);

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != OrderName)
				return;

			Pursuit = Math.Clamp((int)order.ExtraData & 3, 0, 2);
			Tolerance = Math.Clamp((int)(order.ExtraData >> 2) & 3, 0, 2);
			Independence = Math.Clamp((int)(order.ExtraData >> 4) & 3, 0, 2);
		}
	}

	[Desc("The original's tactical AI for a unit (the AIP manual's \"Tactical AI\", the game manual's Unit Behaviours):",
		"pursuit range, damage tolerance and independence, and the standing orders Scout, Harass and Search & Destroy.",
		"Drives AutoTarget's stance and grants a condition for what the unit may pick as a target on its own.")]
	public class DrTacticsInfo : TraitInfo
	{
		[GrantedConditionReference]
		[Desc("Granted while the unit may pick enemy units as targets on its own, but not buildings.")]
		public readonly string UnitsCondition = "tactics-units";

		[GrantedConditionReference]
		[Desc("Granted while the unit may pick units and buildings as targets on its own.")]
		public readonly string AllCondition = "tactics-all";

		[Desc("How far from its post, in cells, a unit with medium pursuit follows an enemy before it turns back.",
			"The manual says only \"a short distance\"; this is the building response radius.")]
		public readonly int PursuitRange = 6;

		[Desc("GENERAL.TXT's SetUnitResponseRadius: tiles around a unit that is hit within which its allies respond.")]
		public readonly int UnitResponseRadius = 3;

		[Desc("GENERAL.TXT's SetBuildingResponseRadius: the same around a building.")]
		public readonly int BuildingResponseRadius = 6;

		[Desc("Ticks a unit waits, idle, before it walks back to its post.")]
		public readonly int ReturnDelay = 40;

		[Desc("Ticks a harassing unit fights after its first shot before it falls back.")]
		public readonly int HarassFightTicks = 75;

		[Desc("Cells a harassing unit falls back.")]
		public readonly int HarassRetreat = 8;

		[Desc("Ticks between new searches for a scouting or hunting unit with nothing to do.")]
		public readonly int SearchInterval = 50;

		public override object Create(ActorInitializer init) { return new DrTactics(init.Self, this); }
	}

	public class DrTactics : ITick, INotifyIdle, IResolveOrder, INotifyCreated, INotifyAttack, INotifyOwnerChanged
	{
		public const string OrderName = "DrSetTactics";

		/// <summary>What a DrSetTactics order sets: its ExtraData is the field shifted 8 and the value.</summary>
		public enum Field { Pursuit, Tolerance, Independence, Order, Guard, Pursue, Default }

		public static uint Pack(Field field, int value = 0) => (uint)field << 8 | (uint)value;

		readonly DrTacticsInfo info;
		readonly Actor self;
		AutoTarget autoTarget;
		AttackBase[] attacks;
		IMove move;
		IHealth health;
		Repairable repairable;
		RepairableNear repairableNear;
		bool mobile;

		public int Pursuit { get; private set; }
		public int Tolerance { get; private set; }
		public int Independence { get; private set; }
		public DrUnitOrder Order { get; private set; }

		/// <summary>Where the unit stood when it last had nothing to do: it returns there after fighting on its own.</summary>
		CPos? home;

		/// <summary>Carrying out a player's (or a script's) order, until it is next idle.</summary>
		bool ordered;
		bool returning;
		bool seekingRepair;
		int idleTicks;
		int searchCooldown;
		int conditionToken = Actor.InvalidConditionToken;
		string granted;

		enum HarassPhase { Seek, Approach, Retreat }
		HarassPhase harass;
		CPos harassTarget;
		long firstShot = -1;

		public DrTactics(Actor self, DrTacticsInfo info)
		{
			this.info = info;
			this.self = self;
		}

		void INotifyCreated.Created(Actor self)
		{
			autoTarget = self.TraitOrDefault<AutoTarget>();
			attacks = self.TraitsImplementing<AttackBase>().ToArray();
			move = self.TraitOrDefault<IMove>();
			health = self.TraitOrDefault<IHealth>();
			repairable = self.TraitOrDefault<Repairable>();
			repairableNear = self.TraitOrDefault<RepairableNear>();

			// Aircraft keep their own idle behaviour (landing, returning to base): they only take the targeting.
			mobile = move != null && !self.Info.HasTraitInfo<AircraftInfo>();
			TakeDefaults(self.Owner);
		}

		void TakeDefaults(Player owner)
		{
			var defaults = owner.PlayerActor.TraitOrDefault<DrTacticsDefaults>();
			Pursuit = defaults?.Pursuit ?? 0;
			Tolerance = defaults?.Tolerance ?? 2;
			Independence = defaults?.Independence ?? 1;
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			TakeDefaults(newOwner);
			Order = DrUnitOrder.None;
		}

		/// <summary>The scenario's SetTactAI: tenacity (pursuit), self preservation (2 - tolerance) and autonomy (independence).</summary>
		public void SetTactAI(int tenacity, int selfPreservation, int autonomy)
		{
			Pursuit = Math.Clamp(tenacity, 0, 2);
			Tolerance = 2 - Math.Clamp(selfPreservation, 0, 2);
			Independence = Math.Clamp(autonomy, 0, 2);
		}

		/// <summary>The scenario's SetSOrderAutoMove, or a script's.</summary>
		public void SetOrder(DrUnitOrder order)
		{
			Order = order;
			harass = HarassPhase.Seek;
			searchCooldown = 0;
			firstShot = -1;
		}

		/// <summary>A script moved the unit, as a player's order would.</summary>
		public void Ordered()
		{
			ordered = true;
			returning = false;
			home = null;
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == OrderName)
			{
				var value = (int)(order.ExtraData & 0xff);
				switch ((Field)(order.ExtraData >> 8))
				{
					case Field.Pursuit: Pursuit = Math.Clamp(value, 0, 2); break;
					case Field.Tolerance: Tolerance = Math.Clamp(value, 0, 2); break;
					case Field.Independence: Independence = Math.Clamp(value, 0, 2); break;
					case Field.Order:
						// Setting an order, or clearing one, stops what the unit was doing.
						if (mobile && Enum.IsDefined(typeof(DrUnitOrder), value) && (value != (int)Order || value != (int)DrUnitOrder.None))
						{
							self.CancelActivity();
							Ordered();
							SetOrder((DrUnitOrder)value);
						}

						break;

					// The presets, as dkreign.exe's handlers set them (0x4b6f20, 0x4b6fb0, 0x4b7040).
					case Field.Guard: (Pursuit, Tolerance, Independence) = (0, 2, 2); break;
					case Field.Pursue: (Pursuit, Tolerance, Independence) = (2, 2, 1); break;
					case Field.Default: TakeDefaults(self.Owner); break;
				}

				return;
			}

			if (order.OrderString == "SetUnitStance" || order.OrderString == DrTacticsDefaults.OrderName)
				return;

			// Any other command ends a standing order and moves the unit's post.
			Order = DrUnitOrder.None;
			Ordered();
		}

		UnitStance Stance => Order == DrUnitOrder.Scout ? UnitStance.HoldFire : Pursuit == 0 ? UnitStance.Defend : UnitStance.AttackAnything;

		/// <summary>What the unit may pick as a target on its own: none, units, or units and buildings.</summary>
		string Targets()
		{
			if (!mobile)
				return info.AllCondition;

			if (Order == DrUnitOrder.Scout)
				return null;

			if (Order != DrUnitOrder.None)
				return info.AllCondition;

			// Carrying out an order: low independence holds its fire, medium shoots at units on the way.
			// Standing: low fires only on units, medium and high on buildings too.
			var busy = ordered && !self.IsIdle;
			return Independence switch
			{
				0 => busy ? null : info.UnitsCondition,
				1 => busy ? info.UnitsCondition : info.AllCondition,
				_ => info.AllCondition,
			};
		}

		void ITick.Tick(Actor self)
		{
			if (autoTarget != null && autoTarget.Stance != Stance)
				autoTarget.SetStance(self, Stance);

			var targets = Targets();
			if (targets != granted)
			{
				if (conditionToken != Actor.InvalidConditionToken)
					conditionToken = self.RevokeCondition(conditionToken);

				if (targets != null)
					conditionToken = self.GrantCondition(targets);

				granted = targets;
			}

			if (!mobile)
				return;

			if (!self.IsIdle)
				idleTicks = 0;

			if (searchCooldown > 0)
				searchCooldown--;

			if (self.World.WorldTick % 10 == 0)
				CheckDamage();

			if (Order == DrUnitOrder.Harass)
				TickHarass();
			else if (Order == DrUnitOrder.None && Pursuit < 2 && !ordered && !returning && !seekingRepair && home != null && !self.IsIdle)
			{
				// Fighting on its own: low and medium pursuit keep to their post.
				var leash = Pursuit == 0 ? info.BuildingResponseRadius : info.PursuitRange;
				if ((self.Location - home.Value).LengthSquared > leash * leash)
				{
					self.CancelActivity();
					ReturnHome();
				}
			}
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			if (!mobile)
				return;

			idleTicks++;
			ordered = false;
			returning = false;

			switch (Order)
			{
				case DrUnitOrder.Scout: Scout(); return;
				case DrUnitOrder.Harass: Harass(); return;
				case DrUnitOrder.SearchAndDestroy: Hunt(); return;
			}

			if (home == null || Pursuit == 2)
			{
				home = self.Location;
				return;
			}

			// After fighting on its own a unit with less than high pursuit goes back (AIP manual, "What happens then?").
			if (idleTicks >= info.ReturnDelay && self.IsIdle && (self.Location - home.Value).LengthSquared > 2)
				ReturnHome();
		}

		void ReturnHome()
		{
			if (home == null)
				return;

			returning = true;
			self.QueueActivity(false, move.MoveTo(home.Value, 1));
		}

		/// <summary>Damage tolerance: low seeks repair when the health bar turns yellow, medium when it turns red.</summary>
		void CheckDamage()
		{
			if (health == null || Tolerance == 2)
				return;

			var threshold = Tolerance == 0 ? 50 : 25;
			var hurt = health.HP * 100 <= health.MaxHP * threshold;
			if (!hurt)
			{
				seekingRepair = false;
				return;
			}

			if (seekingRepair)
				return;

			Actor building = null;
			var closeEnough = new WDist(512);
			if (repairableNear != null)
			{
				building = FindRepair(repairableNear.Info.RepairActors);
				closeEnough = repairableNear.Info.CloseEnough;
			}

			if (building == null && repairable != null)
				building = repairable.FindRepairBuilding(self);

			if (building == null)
				return;

			seekingRepair = true;
			self.QueueActivity(false, new Resupply(self, building, closeEnough));
		}

		Actor FindRepair(System.Collections.Generic.IReadOnlySet<string> types)
		{
			return self.World.ActorsWithTrait<RepairsUnits>()
				.Where(a => !a.Actor.IsDead && a.Actor.IsInWorld && a.Actor.Owner.IsAlliedWith(self.Owner) && types.Contains(a.Actor.Info.Name))
				.Select(a => a.Actor)
				.ClosestToIgnoringPath(self);
		}

		/// <summary>
		/// The AIP manual's projectile response: a unit near one that was hit runs at the shooter, or away from it if
		/// it cannot hurt it, then goes back to its post. Units carrying out orders, or with low independence, do not.
		/// </summary>
		public void Respond(Actor attacker, int radius)
		{
			if (!mobile || Independence == 0 || Order != DrUnitOrder.None || seekingRepair || !(self.IsIdle || returning))
				return;

			if (attacker == null || attacker.IsDead || !attacker.IsInWorld || !attacker.AppearsHostileTo(self))
				return;

			home ??= self.Location;
			returning = false;
			var target = Target.FromActor(attacker);
			var attack = attacks.FirstOrDefault(a => !a.IsTraitDisabled && a.HasAnyValidWeapons(target));
			if (attack != null)
			{
				attack.AttackTarget(target, AttackSource.AutoTarget, false, true);
				return;
			}

			var cell = AwayFrom(attacker.Location, Math.Max(radius, 4));
			if (cell != null)
				self.QueueActivity(false, move.MoveTo(cell.Value, 2));
		}

		bool Reachable(CPos cell)
		{
			var locomotor = (move as Mobile)?.Locomotor;
			var pathFinder = self.World.WorldActor.TraitOrDefault<IPathFinder>();
			return self.World.Map.Contains(cell) && (locomotor == null || pathFinder == null || pathFinder.PathExistsForLocomotor(locomotor, self.Location, cell));
		}

		/// <summary>A cell it can reach, up to that far from the unit directly away from a place, or nearly so.</summary>
		CPos? AwayFrom(CPos from, int distance)
		{
			var away = self.Location - from;
			if (away == CVec.Zero)
				away = new CVec(0, 1);

			var length = Math.Max(1, away.Length);
			for (var d = distance; d >= 2; d--)
			{
				// Straight away, then turned a little either side.
				var x = away.X * d / length;
				var y = away.Y * d / length;
				foreach (var v in new[] { new CVec(x, y), new CVec(x - y / 2, y + x / 2), new CVec(x + y / 2, y - x / 2) })
					if (Reachable(self.Location + v))
						return self.Location + v;
			}

			return null;
		}

		void Scout()
		{
			if (searchCooldown > 0)
				return;

			searchCooldown = info.SearchInterval;

			// The nearest of a few unexplored cells it can reach; anywhere, once the map is explored.
			var map = self.World.Map;
			var shroud = self.Owner.Shroud;
			CPos? best = null;
			var bestDistance = int.MaxValue;
			for (var i = 0; i < 24; i++)
			{
				var cell = map.ChooseRandomCell(self.World.SharedRandom);
				if (!Reachable(cell))
					continue;

				var distance = (cell - self.Location).LengthSquared;
				var explored = shroud.IsExplored(cell);
				if (explored)
					distance += 1 << 24;

				if (distance < bestDistance)
				{
					best = cell;
					bestDistance = distance;
				}
			}

			if (best != null)
				self.QueueActivity(false, move.MoveTo(best.Value, 3));
		}

		void Hunt()
		{
			if (searchCooldown > 0 || !attacks.Any())
				return;

			searchCooldown = info.SearchInterval;
			self.QueueActivity(false, new Hunt(self));
		}

		Actor ClosestEnemy()
		{
			return self.World.ActorsHavingTrait<Huntable>()
				.Where(a => a != self && !a.IsDead && a.IsInWorld && a.AppearsHostileTo(self) && a.IsTargetableBy(self)
					&& attacks.Any(ab => ab.HasAnyValidWeapons(Target.FromActor(a))))
				.ClosestToIgnoringPath(self);
		}

		/// <summary>Harass: find the enemy, fight briefly, fall back, again.</summary>
		void Harass()
		{
			// Back from falling back: a pause, then the next run.
			if (harass == HarassPhase.Retreat)
			{
				harass = HarassPhase.Seek;
				searchCooldown = info.SearchInterval;
				return;
			}

			if (searchCooldown > 0)
				return;

			var enemy = ClosestEnemy();
			if (enemy == null)
			{
				searchCooldown = info.SearchInterval;
				return;
			}

			harass = HarassPhase.Approach;
			harassTarget = enemy.Location;
			self.QueueActivity(false, new AttackMoveActivity(self,
				() => move.MoveWithinRange(Target.FromCell(self.World, enemy.Location), WDist.FromCells(2))));
		}

		void TickHarass()
		{
			// Fighting, however it started: after a while, away.
			if (harass == HarassPhase.Retreat || firstShot < 0 || self.World.WorldTick - firstShot < info.HarassFightTicks)
				return;

			firstShot = -1;
			var cell = AwayFrom(harassTarget, info.HarassRetreat);
			if (cell == null)
				return;

			harass = HarassPhase.Retreat;
			self.CancelActivity();
			self.QueueActivity(false, move.MoveTo(cell.Value, 2));
		}

		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel barrel)
		{
			if (Order == DrUnitOrder.Harass && harass != HarassPhase.Retreat && firstShot < 0)
			{
				firstShot = self.World.WorldTick;
				if (target.Type == TargetType.Actor)
					harassTarget = target.Actor.Location;
			}
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel barrel) { }
	}

	[Desc("Tells nearby units with DrTactics that this actor was hit, for the original's projectile response.")]
	public class DrCallsForHelpInfo : TraitInfo
	{
		[Desc("Tiles: GENERAL.TXT's SetUnitResponseRadius (3) for units, SetBuildingResponseRadius (6) for buildings.")]
		public readonly int Radius = 3;

		public override object Create(ActorInitializer init) { return new DrCallsForHelp(this); }
	}

	public class DrCallsForHelp : INotifyDamage
	{
		readonly DrCallsForHelpInfo info;
		long lastCall = -100;

		public DrCallsForHelp(DrCallsForHelpInfo info)
		{
			this.info = info;
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			var attacker = e.Attacker;
			if (e.Damage.Value <= 0 || attacker == null || attacker.IsDead || !attacker.IsInWorld || attacker.Owner.IsAlliedWith(self.Owner))
				return;

			// Once a second is enough: a burst of fire need not search for helpers with every hit.
			if (self.World.WorldTick - lastCall < 25)
				return;

			lastCall = self.World.WorldTick;
			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, WDist.FromCells(info.Radius)))
				if (a != self && !a.IsDead && a.Owner.IsAlliedWith(self.Owner))
					a.TraitOrDefault<DrTactics>()?.Respond(attacker, info.Radius);
		}
	}
}
