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
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Dr.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Activities
{
	/// <summary>
	/// The engine's FindAndDeliverResources for a <see cref="DrFreighter"/>: it fetches only the freighter's cargo,
	/// delivers only where that is taken, and turns to the other resource when nothing takes its own. It derives
	/// from the engine's so that Harvester and the bots see a freighter at work, but none of that runs.
	/// </summary>
	public class DrFindAndDeliverResources : FindAndDeliverResources
	{
		readonly DrFreighter harv;
		readonly DrFreighterInfo harvInfo;
		readonly Mobile mobile;
		readonly ResourceClaimLayer claimLayer;
		readonly DockClientManager dockClient;
		readonly MoveCooldownHelper moveCooldownHelper;
		CPos? orderLocation;
		CPos? lastHarvestedCell;
		bool hasDeliveredLoad;
		bool hasHarvestedCell;
		bool hasWaited;
		bool lastSearchFailed;

		public DrFindAndDeliverResources(Actor self, CPos? orderLocation = null)
			: base(self, orderLocation)
		{
			harv = self.Trait<DrFreighter>();
			harvInfo = self.Info.TraitInfo<DrFreighterInfo>();
			dockClient = self.Trait<DockClientManager>();
			mobile = self.Trait<Mobile>();
			claimLayer = self.World.WorldActor.Trait<ResourceClaimLayer>();
			moveCooldownHelper = new MoveCooldownHelper(self.World, mobile) { RetryIfDestinationBlocked = true };
			this.orderLocation = orderLocation;
		}

		protected override void OnFirstRun(Actor self)
		{
			if (orderLocation != null)
			{
				lastHarvestedCell = orderLocation;
				if (harv.IsFull || (!harv.IsEmpty && harv.Carrying != harv.Cargo))
					QueueChild(new MoveToDock(self, dockLineColor: dockClient.DockLineColor));
			}
		}

		/// <summary>
		/// With nowhere to take the cargo (the generators are full, or there are none) but somewhere to take the
		/// other resource, the freighter turns to that, throwing away a load nothing will take.
		/// </summary>
		void ChooseCargo(Actor self)
		{
			var carrying = harv.Carrying;
			var resource = carrying ?? harv.Cargo;
			if (harv.CanDeliver(self, resource))
				return;

			var other = harvInfo.Resources.FirstOrDefault(r => r != resource && harv.CanDeliver(self, r));
			if (other == null)
				return;

			if (carrying != null)
				harv.Discard();

			harv.Cargo = other;
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling || harv.IsTraitDisabled)
				return true;

			if (NextActivity != null)
			{
				// Interrupt automated harvesting after clearing the first cell.
				if (!harvInfo.QueueFullLoad && (hasHarvestedCell || lastSearchFailed))
					return true;

				// Interrupt automated harvesting after first complete harvest cycle.
				if (hasDeliveredLoad || harv.IsFull)
					return true;
			}

			ChooseCargo(self);

			// Are we full, carrying the wrong resource, or have nothing more to gather? Deliver resources.
			if (harv.IsFull || (!harv.IsEmpty && (lastSearchFailed || harv.Carrying != harv.Cargo)))
			{
				// If we are reserved it means docking was already initiated and we should wait.
				if (harv.DockClientManager.ReservedHost != null)
					return false;

				if (harv.CanDeliver(self, harv.Carrying))
				{
					QueueChild(new MoveToDock(self, dockLineColor: dockClient.DockLineColor));
					hasDeliveredLoad = true;
					return false;
				}

				// Nowhere takes the load, nor anything else: wait for that to change.
				QueueChild(new Wait(harv.Info.WaitDuration));
				return false;
			}

			// After a failed search, wait and sit still for a bit before searching again.
			if (lastSearchFailed && !hasWaited)
			{
				QueueChild(new Wait(harv.Info.WaitDuration));
				hasWaited = true;
				return false;
			}

			hasWaited = false;

			// Scan for resources. If no resources are found near the current field, search near the refinery
			// instead. If that doesn't help, give up for now.
			var closestHarvestableCell = ClosestHarvestablePos(self);
			if (!closestHarvestableCell.HasValue)
			{
				if (lastHarvestedCell != null)
				{
					lastHarvestedCell = null; // Forces search from backup position.
					closestHarvestableCell = ClosestHarvestablePos(self);
					lastSearchFailed = !closestHarvestableCell.HasValue;
				}
				else
					lastSearchFailed = true;
			}
			else
				lastSearchFailed = false;

			var result = moveCooldownHelper.Tick(false);
			if (result != null)
				return result.Value;

			// If no harvestable position could be found and we are at the refinery, get out of the way
			// of the refinery entrance.
			if (lastSearchFailed)
			{
				var lastproc = harv.DockClientManager?.LastReservedHost;
				if (lastproc != null)
				{
					var deliveryLoc = self.World.Map.CellContaining(lastproc.DockPosition);
					if (self.Location == deliveryLoc && harv.IsEmpty)
					{
						var unblockCell = deliveryLoc + harv.Info.UnblockCell;
						var moveTo = mobile.NearestMoveableCell(unblockCell, 1, 5);
						moveCooldownHelper.NotifyMoveQueued();
						QueueChild(mobile.MoveTo(moveTo, 1));
					}
				}

				return false;
			}

			// If we get here, our search for resources was successful. Commence harvesting.
			moveCooldownHelper.NotifyMoveQueued();
			QueueChild(new DrHarvestResource(self, closestHarvestableCell.Value));
			lastHarvestedCell = closestHarvestableCell.Value;
			hasHarvestedCell = true;
			return false;
		}

		/// <summary>
		/// Finds the closest harvestable pos between the current position of the harvester
		/// and the last order location.
		/// </summary>
		CPos? ClosestHarvestablePos(Actor self)
		{
			// Harvesters should respect an explicit harvest order instead of harvesting the current cell.
			if (orderLocation == null)
			{
				if (harv.CanHaulCell(self.Location) && claimLayer.CanClaimCell(self, self.Location))
					return self.Location;
			}
			else
			{
				if (harv.CanHaulCell(orderLocation.Value) && claimLayer.CanClaimCell(self, orderLocation.Value))
					return orderLocation;

				orderLocation = null;
			}

			// Determine where to search from and how far to search:
			// Prioritise search by these locations in this order: lastHarvestedCell -> lastLinkedDock -> self.
			CPos searchFromLoc;
			int searchRadius;
			var dockPos = harv.DockClientManager?.LastReservedHost?.DockPosition;

			if (lastHarvestedCell.HasValue)
			{
				searchRadius = harvInfo.SearchFromHarvesterRadius;
				searchFromLoc = lastHarvestedCell.Value;
			}
			else
			{
				searchRadius = harvInfo.SearchFromProcRadius;
				if (dockPos != null)
					searchFromLoc = self.World.Map.CellContaining(dockPos.Value);
				else
					searchFromLoc = self.Location;
			}

			var searchRadiusSquared = searchRadius * searchRadius;

			var map = self.World.Map;
			var harvPos = self.CenterPosition;

			// Find any harvestable resources:
			var path = mobile.PathFinder.FindPathToTargetCellByPredicate(
				self,
				[searchFromLoc, self.Location],
				loc =>
					harv.CanHaulCell(loc) &&
					claimLayer.CanClaimCell(self, loc),
				BlockedByActor.Stationary,
				loc =>
				{
					if ((loc - searchFromLoc).LengthSquared > searchRadiusSquared)
						return PathGraph.PathCostForInvalidPath;

					// Add a cost modifier to harvestable cells to prefer resources that are closer to the refinery.
					// This reduces the tendency for harvesters to move in straight lines
					if (dockPos.HasValue && harvInfo.ResourceRefineryDirectionPenalty > 0 && harv.CanHaulCell(loc))
					{
						var pos = map.CenterOfCell(loc);

						// Calculate harv-cell-refinery angle (cosine rule)
						var b = pos - dockPos.Value;

						if (b != WVec.Zero)
						{
							var c = pos - harvPos;
							if (c != WVec.Zero)
							{
								var a = harvPos - dockPos.Value;
								var cosA = (int)(512 * (b.LengthSquared + c.LengthSquared - a.LengthSquared) / b.Length / c.Length);

								// Cost modifier varies between 0 and ResourceRefineryDirectionPenalty
								return Math.Abs(harvInfo.ResourceRefineryDirectionPenalty / 2) + harvInfo.ResourceRefineryDirectionPenalty * cosA / 2048;
							}
						}
					}

					return 0;
				});

			if (path.Count > 0)
				return path[0];

			return null;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return Target.FromCell(self.World, self.Location);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (ChildActivity != null)
				foreach (var n in ChildActivity.TargetLineNodes(self))
					yield return n;

			if (orderLocation != null)
				yield return new TargetLineNode(Target.FromCell(self.World, orderLocation.Value), harvInfo.HarvestLineColor);
			else
			{
				var manager = harv.DockClientManager;
				if (manager?.ReservedHostActor != null)
					yield return new TargetLineNode(Target.FromActor(manager.ReservedHostActor), manager.DockLineColor);
			}
		}
	}
}
