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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Dr.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Activities
{
	/// <summary>The engine's HarvestResource for a <see cref="DrFreighter"/>: it loads only the freighter's cargo.</summary>
	public class DrHarvestResource : Activity
	{
		readonly DrFreighter harv;
		readonly DrFreighterInfo harvInfo;
		readonly IFacing facing;
		readonly ResourceClaimLayer claimLayer;
		readonly IResourceLayer resourceLayer;
		readonly BodyOrientation body;
		readonly IMove move;
		readonly CPos targetCell;
		readonly INotifyHarvestAction[] notifyHarvestActions;
		readonly MoveCooldownHelper moveCooldownHelper;

		public DrHarvestResource(Actor self, CPos targetCell)
		{
			harv = self.Trait<DrFreighter>();
			harvInfo = self.Info.TraitInfo<DrFreighterInfo>();
			facing = self.Trait<IFacing>();
			body = self.Trait<BodyOrientation>();
			move = self.Trait<IMove>();
			claimLayer = self.World.WorldActor.Trait<ResourceClaimLayer>();
			resourceLayer = self.World.WorldActor.Trait<IResourceLayer>();
			this.targetCell = targetCell;
			notifyHarvestActions = self.TraitsImplementing<INotifyHarvestAction>().ToArray();
			moveCooldownHelper = new MoveCooldownHelper(self.World, move as Mobile);
		}

		protected override void OnFirstRun(Actor self)
		{
			// We can safely assume the claim is successful, since this is only called in the
			// same actor-tick as the targetCell is selected. Therefore no other harvester
			// would have been able to claim.
			claimLayer.TryClaimCell(self, targetCell);
		}

		public override bool Tick(Actor self)
		{
			if (harv.IsTraitDisabled)
				Cancel(self, true);

			// A load of the other resource goes home before this one is started.
			if (IsCanceling || harv.IsFull || (!harv.IsEmpty && harv.Carrying != harv.Cargo))
				return true;

			var result = moveCooldownHelper.Tick(false);
			if (result != null)
				return result.Value;

			// Move towards the target cell
			if (self.Location != targetCell)
			{
				foreach (var n in notifyHarvestActions)
					n.MovingToResources(self, targetCell);

				moveCooldownHelper.NotifyMoveQueued();
				QueueChild(move.MoveTo(targetCell, 0));
				return false;
			}

			if (!harv.CanHaulCell(self.Location))
				return true;

			// Turn to one of the harvestable facings
			if (harvInfo.HarvestFacings != 0)
			{
				var current = facing.Facing;
				var desired = body.QuantizeFacing(current, harvInfo.HarvestFacings);
				if (desired != current)
				{
					QueueChild(new Turn(self, desired));
					return false;
				}
			}

			var resource = resourceLayer.GetResource(self.Location);
			if (resource.Type == null || resourceLayer.RemoveResource(resource.Type, self.Location) != 1)
				return true;

			harv.AddResource(self, resource.Type);

			foreach (var t in notifyHarvestActions)
				t.Harvested(self, resource.Type);

			QueueChild(new Wait(harvInfo.BaleLoadDelay));
			return false;
		}

		protected override void OnLastRun(Actor self)
		{
			claimLayer.RemoveClaim(self);
		}

		public override void Cancel(Actor self, bool keepQueue = false)
		{
			foreach (var n in notifyHarvestActions)
				n.MovementCancelled(self);

			base.Cancel(self, keepQueue);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			yield return new TargetLineNode(Target.FromCell(self.World, targetCell), harvInfo.HarvestLineColor);
		}
	}
}
