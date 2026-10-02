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
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Dr.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[Desc("The original's freighter: it hauls one resource at a time (its cargo) to the buildings that store it,",
		"water to launch pads and taelon to generators. A freighter takes the cargo of the building that made it;",
		"a harvest order on a spring changes it. Set SearchOnCreation: false: this trait starts its own search.")]
	public class DrFreighterInfo : HarvesterInfo
	{
		[Desc("The cargo of a freighter no storing building made.")]
		public readonly string DefaultResource = "Water";

		[Desc("Start hauling when created.")]
		public readonly bool HaulOnCreation = true;

		public override object Create(ActorInitializer init) { return new DrFreighter(init, this); }
	}

	public class DrFreighter : Harvester, IResolveOrder
	{
		readonly DrFreighterInfo info;
		readonly IStoresResources store;
		readonly IResourceLayer resourceLayer;
		readonly ResourceClaimLayer claimLayer;
		readonly Lazy<Actor> parent;
		Mobile mobile;

		/// <summary>The resource this freighter fetches.</summary>
		public string Cargo { get; set; }

		public DrFreighter(ActorInitializer init, DrFreighterInfo info)
			: base(init.Self, info)
		{
			this.info = info;
			Cargo = info.DefaultResource;
			store = init.Self.TraitsImplementing<IStoresResources>().First(s => info.Resources.Any(s.HasType));
			resourceLayer = init.World.WorldActor.Trait<IResourceLayer>();
			claimLayer = init.World.WorldActor.Trait<ResourceClaimLayer>();
			parent = init.GetOrDefault<ParentActorInit>()?.Value.Actor(init.World);
		}

		/// <summary>The resource on board, or null when empty.</summary>
		public string Carrying => store.Contents.FirstOrDefault(c => c.Value > 0).Key;

		/// <summary>A cell holding this freighter's cargo.</summary>
		public bool CanHaulCell(CPos cell) => CanHarvestCell(cell) && resourceLayer.GetResource(cell).Type == Cargo;

		/// <summary>Whether one of the owner's buildings can take <paramref name="resource"/> now.</summary>
		public bool CanDeliver(Actor self, string resource) => self.Owner.Refineries(resource).Any(r => r.Trait.CanAccept(resource));

		/// <summary>Throws the load away, when nothing will ever take it.</summary>
		public void Discard()
		{
			foreach (var c in store.Contents.ToList())
				store.RemoveResource(c.Key, c.Value);
		}

		protected override void Created(Actor self)
		{
			mobile = self.TraitOrDefault<Mobile>();
			var refinery = parent?.Value?.TraitOrDefault<DrRefinery>();
			if (refinery != null && info.Resources.Contains(refinery.Info.Resource))
				Cargo = refinery.Info.Resource;

			base.Created(self);

			if (info.HaulOnCreation && mobile != null)
				self.QueueActivity(new DrFindAndDeliverResources(self));
		}

		public override bool CanDockAt(Actor hostActor, IDockHost host, bool forceEnter = false, bool ignoreOccupancy = false)
		{
			var refinery = hostActor.TraitOrDefault<DrRefinery>();
			return base.CanDockAt(hostActor, host, forceEnter, ignoreOccupancy)
				&& refinery != null && refinery.CanAccept(Carrying ?? Cargo);
		}

		public override bool OnDockTick(Actor self, Actor hostActor, IDockHost host)
		{
			// A store that fills while unloading sends the freighter away with the rest, instead of keeping it docked.
			var refinery = hostActor.TraitOrDefault<DrRefinery>();
			if (refinery != null && !IsEmpty && !refinery.CanAccept(Carrying))
				return true;

			return base.OnDockTick(self, hostActor, host);
		}

		public override void OnDockCompleted(Actor self, Actor hostActor, IDockHost dock)
		{
			// Harvester's own would queue the engine's search, which hauls any resource.
			if (!GetDockType.Overlaps(dock.GetDockType))
				return;

			var current = self.CurrentActivity;
			if (current == null || (current is not FindAndDeliverResources && current.NextActivity == null))
				self.QueueActivity(true, new DrFindAndDeliverResources(self));
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "Harvest" || mobile == null)
				return;

			CPos loc;
			if (order.Target.Type != TargetType.Invalid)
			{
				// A harvest order on a spring makes its resource the cargo.
				var cell = self.World.Map.CellContaining(order.Target.CenterPosition);
				var type = resourceLayer.GetResource(cell).Type;
				if (type != null && info.Resources.Contains(type))
					Cargo = type;

				loc = mobile.NearestCell(cell, p => mobile.CanEnterCell(p) && claimLayer.TryClaimCell(self, p), 1, 6);
			}
			else
				loc = self.Location;

			self.QueueActivity(order.Queued, new DrFindAndDeliverResources(self, loc));
			self.ShowTargetLines();
		}
	}
}
