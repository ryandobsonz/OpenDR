#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[Desc("The player's water: what the launch pads hold, and the forced sale",
		"(the Sell Water button, or a double click on the credits).")]
	public class DrPlayerResourcesInfo : TraitInfo
	{
		[Desc("The resource the launch pads sell.")]
		public readonly string Water = "Water";

		public override object Create(ActorInitializer init) { return new DrPlayerResources(init.Self, this); }
	}

	public class DrPlayerResources : IResolveOrder
	{
		public const string SellWaterOrder = "DrSellWater";

		readonly DrPlayerResourcesInfo info;
		readonly Player owner;

		public DrPlayerResources(Actor self, DrPlayerResourcesInfo info)
		{
			this.info = info;
			owner = self.Owner;
		}

		/// <summary>How full the player's launch pads are, together: the water bar.</summary>
		public float WaterFraction
		{
			get
			{
				var pads = owner.Refineries(info.Water).ToList();
				var capacity = pads.Sum(p => p.Trait.Info.Capacity);
				return capacity == 0 ? 0 : (float)pads.Sum(p => p.Trait.Stored) / capacity;
			}
		}

		/// <summary>What a forced sale would cost now: a fee for each pad that has enough water to launch.</summary>
		public int ForcedSaleCost => owner.Refineries(info.Water)
			.Where(p => p.Trait.Stored * p.Trait.Info.SalePercent / 100 > p.Trait.Info.ForcedSaleFee)
			.Sum(p => p.Trait.Info.ForcedSaleFee);

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != SellWaterOrder)
				return;

			var credits = 0;
			foreach (var p in owner.Refineries(info.Water).ToList())
				credits += p.Trait.Sell(p.Trait.Info.ForcedSaleFee);

			Log.Write("debug", $"{owner.InternalName}: forced water sale for {credits} credits");
		}
	}
}
