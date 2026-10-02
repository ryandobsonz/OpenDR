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

using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	/// <summary>A building finished from its construction (SelfConstructing), counted once already as it began.</summary>
	public class CompletedConstructionInit : RuntimeFlagInit, ISingleInstanceInit { }

	[Desc("Counts the actor in the debrief's statistics (DrMissionStatistics): a building or, if it moves, a unit.")]
	public class DrCountsInStatisticsInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new DrCountsInStatistics(init); }
	}

	public class DrCountsInStatistics : INotifyCreated, INotifyKilled
	{
		readonly bool completed;
		DrMissionStatistics statistics;
		bool building;
		bool counted;

		public DrCountsInStatistics(ActorInitializer init)
		{
			completed = init.Contains<CompletedConstructionInit>();
		}

		void INotifyCreated.Created(Actor self)
		{
			statistics = self.World.WorldActor.TraitOrDefault<DrMissionStatistics>();
			building = self.Info.HasTraitInfo<BuildingInfo>();
			counted = statistics != null && (building || self.Info.HasTraitInfo<IPositionableInfo>());
			if (!counted || completed)
				return;

			var s = statistics[self.Owner];
			if (building)
				s.BuildingsCreated++;
			else
				s.UnitsCreated++;
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (!counted)
				return;

			var lost = statistics[self.Owner];
			var killer = e.Attacker?.Owner;
			var destroyed = killer != null && killer != self.Owner ? statistics[killer] : null;
			if (building)
			{
				lost.BuildingsLost++;
				if (destroyed != null)
					destroyed.BuildingsDestroyed++;
			}
			else
			{
				lost.UnitsLost++;
				if (destroyed != null)
					destroyed.UnitsDestroyed++;
			}
		}
	}
}
