#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
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
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[Desc("A building freighters deliver one resource to, which it stores: the original's SetResource.",
		"The Water Launch Pad sells its water when the tank is full (SetResourceSale); the Taelon Power Generator's",
		"power follows its taelon (SupplyResource). Amounts are in the resource's own units, which",
		"PlayerResources.ResourceValues gives per bale.")]
	public class DrRefineryInfo : TraitInfo, Requires<IDockHostInfo>
	{
		[FieldLoader.Require]
		[Desc("The resource this building takes.")]
		public readonly string Resource = null;

		[Desc("How much it holds.")]
		public readonly int Capacity = 3000;

		[Desc("How much it holds when built.")]
		public readonly int Initial = 0;

		[Desc("Sell the store when it is full, at this percentage of its amount in credits. 0 never sells.")]
		public readonly int SalePercent = 0;

		[Desc("What a forced sale costs.")]
		public readonly int ForcedSaleFee = 500;

		[Desc("Scale the building's Power by how full the store is.")]
		public readonly bool ScalesPower = false;

		[Desc("Colour of the store's bar under the selection box.")]
		public readonly Color BarColor = Color.FromArgb(255, 56, 120, 232);

		[NotificationReference("Sounds")]
		public readonly string SaleNotification = "CreditsReceived";

		[FluentReference("amount")]
		public readonly string SaleTextNotification = "notification-water-launched";

		public override object Create(ActorInitializer init) { return new DrRefinery(init.Self, this); }
	}

	public class DrRefinery : IAcceptResources, IPowerModifier, ISelectionBar, INotifyOwnerChanged, ISync
	{
		public readonly DrRefineryInfo Info;
		readonly Actor self;
		PlayerResources playerResources;
		PowerManager power;

		[VerifySync]
		public int Stored { get; private set; }

		public DrRefinery(Actor self, DrRefineryInfo info)
		{
			Info = info;
			this.self = self;
			Stored = Math.Min(info.Initial, info.Capacity);
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
			power = self.Owner.PlayerActor.TraitOrDefault<PowerManager>();
		}

		public bool IsFull => Stored >= Info.Capacity;

		int ValuePerBale => playerResources.Info.ResourceValues.GetValueOrDefault(Info.Resource, 1);

		/// <summary>Whether a freighter carrying <paramref name="resourceType"/> can unload here now.</summary>
		public bool CanAccept(string resourceType) => resourceType == Info.Resource && Info.Capacity - Stored >= ValuePerBale;

		int IAcceptResources.AcceptResources(Actor self, string resourceType, int count)
		{
			if (resourceType != Info.Resource)
				return 0;

			count = Math.Min(count, (Info.Capacity - Stored) / ValuePerBale);
			if (count <= 0)
				return 0;

			var amount = count * ValuePerBale;
			Stored += amount;

			foreach (var notify in self.World.ActorsWithTrait<INotifyResourceAccepted>())
				if (notify.Actor.Owner == self.Owner || notify.Actor == self.World.WorldActor)
					notify.Trait.OnResourceAccepted(notify.Actor, self, resourceType, count, amount);

			if (Info.ScalesPower)
				power?.UpdateActor(self);

			if (Info.SalePercent > 0 && IsFull)
				Sell(0);

			return count;
		}

		/// <summary>Launches the store for credits, less <paramref name="fee"/>; what it brought in.</summary>
		public int Sell(int fee)
		{
			var credits = Stored * Info.SalePercent / 100 - fee;
			if (credits <= 0)
				return 0;

			Stored = 0;
			playerResources.GiveCash(credits);

			var owner = self.Owner;
			Game.Sound.PlayNotification(self.World.Map.Rules, owner, "Sounds", Info.SaleNotification, null);
			TextNotificationsManager.AddTransientLine(owner, FluentProvider.GetMessage(Info.SaleTextNotification, "amount", credits));
			if (owner.IsAlliedWith(self.World.RenderPlayer))
				self.World.AddFrameEndTask(w => w.Add(new FloatingText(self.CenterPosition, self.OwnerColor(), FloatingText.FormatCashTick(credits), 30)));

			return credits;
		}

		int IPowerModifier.GetPowerModifier() => Info.ScalesPower ? 100 * Stored / Info.Capacity : 100;

		float ISelectionBar.GetValue() => (float)Stored / Info.Capacity;
		Color ISelectionBar.GetColor() => Info.BarColor;
		bool ISelectionBar.DisplayWhenEmpty => true;

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			playerResources = newOwner.PlayerActor.Trait<PlayerResources>();
			power = newOwner.PlayerActor.TraitOrDefault<PowerManager>();
		}
	}

	public static class DrRefineryExts
	{
		/// <summary>The player's buildings that store <paramref name="resource"/>.</summary>
		public static IEnumerable<TraitPair<DrRefinery>> Refineries(this Player player, string resource)
		{
			return player.World.ActorsWithTrait<DrRefinery>()
				.Where(p => p.Actor.Owner == player && !p.Actor.IsDead && p.Actor.IsInWorld && p.Trait.Info.Resource == resource);
		}
	}
}
