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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[Desc("A facility whose plans an infiltrator steals: what the original game's tables say it makes,",
		"through DrScenarioScript, else what it produces here.")]
	public class DrPlanSourceInfo : TraitInfo<DrPlanSource> { }

	public class DrPlanSource : INotifyInfiltrated
	{
		void INotifyInfiltrated.Infiltrated(Actor self, Actor infiltrator, BitSet<TargetableType> types)
		{
			var carrier = infiltrator.TraitOrDefault<DrCarriesPlans>();
			if (carrier == null)
				return;

			var original = self.World.WorldActor.TraitOrDefault<DrScenarioScript>()?.PlansFor(self.Info.Name);
			carrier.Carry(original ?? self.TraitsImplementing<ProductionQueue>().SelectMany(q => q.AllItems()).Select(a => a.Name));
		}
	}

	[Desc("An infiltrator that carries stolen plans home: they count as stolen once it reaches its own headquarters,",
		"as Dark Reign's CritStealPlan requires.")]
	public class DrCarriesPlansInfo : TraitInfo
	{
		[Desc("Cells from its headquarters at which the plans are handed over.")]
		public readonly int DeliveryRange = 4;

		public override object Create(ActorInitializer init) { return new DrCarriesPlans(this); }
	}

	public class DrCarriesPlans : ITick
	{
		readonly DrCarriesPlansInfo info;
		readonly HashSet<string> carried = new();
		int countdown;

		public DrCarriesPlans(DrCarriesPlansInfo info) { this.info = info; }

		public void Carry(IEnumerable<string> plans)
		{
			carried.UnionWith(plans);
		}

		void ITick.Tick(Actor self)
		{
			if (carried.Count == 0 || --countdown > 0)
				return;

			countdown = 10;
			var range = (info.DeliveryRange + 1) * (info.DeliveryRange + 1);
			var map = self.World.Map;
			var home = self.World.ActorsHavingTrait<Building>().Any(a => a.Owner == self.Owner && !a.IsDead
				&& a.Info.Name.StartsWith("hq.", System.StringComparison.Ordinal)
				&& (map.CellContaining(a.CenterPosition) - self.Location).LengthSquared <= range);
			if (!home)
				return;

			self.World.WorldActor.TraitOrDefault<DrScenarioScript>()?.PlansStolen(self.Owner, carried);
			if (self.Owner == self.World.LocalPlayer)
				TextNotificationsManager.AddMissionLine("Mission", "Plans stolen.", Color.White);

			carried.Clear();
		}
	}
}
