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
	[Desc("Tells DrScenarioScript where damage is done and by whom, for the original's harass-a-region condition.")]
	public class DrReportsDamageInfo : TraitInfo<DrReportsDamage> { }

	public class DrReportsDamage : INotifyDamage
	{
		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			self.World.WorldActor.TraitOrDefault<DrScenarioScript>()?.ReportDamage(self, e);
		}
	}
}
