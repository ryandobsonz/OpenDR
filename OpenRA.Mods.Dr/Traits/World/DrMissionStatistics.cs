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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	/// <summary>A team's figures for the debrief, in the original's order (its SS_STAT_* strings).</summary>
	public sealed class DrTeamStatistics
	{
		public int WaterCollected, TaelonCollected;
		public int UnitsCreated, BuildingsCreated;
		public int UnitsLost, BuildingsLost;
		public int UnitsDestroyed, BuildingsDestroyed;

		/// <summary>The debrief's columns: water and taelon collected; units and buildings created, lost and destroyed.</summary>
		public int[] Columns =>
			[WaterCollected, TaelonCollected, UnitsCreated, BuildingsCreated, UnitsLost, BuildingsLost, UnitsDestroyed, BuildingsDestroyed];
	}

	[TraitLocation(SystemActors.World)]
	[Desc("Counts what each player collected, created, lost and destroyed in a mission, as the original's debrief shows it.",
		"Units and buildings report to it through DrCountsInStatistics.")]
	public class DrMissionStatisticsInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new DrMissionStatistics(); }
	}

	/// <summary>
	/// The original (dkreign.exe) keeps these per team: a unit or building counts as created when it comes into
	/// being, the mission's own included, as lost when it dies, and as destroyed for the team that killed it,
	/// unless that was its own. Resources count as they are delivered, water and taelon apart, in their own units.
	/// </summary>
	public class DrMissionStatistics : INotifyResourceAccepted
	{
		readonly Dictionary<Player, DrTeamStatistics> players = new();

		public DrTeamStatistics this[Player p]
		{
			get
			{
				if (!players.TryGetValue(p, out var s))
					players[p] = s = new DrTeamStatistics();

				return s;
			}
		}

		void INotifyResourceAccepted.OnResourceAccepted(Actor self, Actor refinery, string resourceType, int count, int value)
		{
			var s = this[refinery.Owner];
			if (resourceType == "Taelon")
				s.TaelonCollected += value;
			else
				s.WaterCollected += value;
		}
	}
}
