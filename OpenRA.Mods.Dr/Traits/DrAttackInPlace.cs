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

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[Desc("The original's Attack Without Moving (the top bar's button, shift A): attack the target only while it is in range,",
		"without closing on it or following it.")]
	public class DrAttackInPlaceInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new DrAttackInPlace(); }
	}

	public class DrAttackInPlace : IResolveOrder, IOrderVoice
	{
		public const string OrderName = "DrAttackInPlace";

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != OrderName || !order.Target.IsValidFor(self))
				return;

			var attack = self.TraitsImplementing<AttackBase>().FirstOrDefault(a => !a.IsTraitDisabled);
			attack?.AttackTarget(order.Target, AttackSource.Default, order.Queued, false, true);
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			return order.OrderString == OrderName ? self.TraitsImplementing<AttackBase>().FirstOrDefault()?.Info.Voice : null;
		}
	}
}
