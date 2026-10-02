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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Orders;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Orders
{
	/// <summary>
	/// An order the interface's buttons give the selection, then a click on the map: the actor under the
	/// mouse (when actors are targets) or the ground. Attack Without Moving, Set Exit Point.
	/// </summary>
	public class DrTargetOrderGenerator : UnitOrderGenerator
	{
		readonly string orderName;
		readonly string cursor;
		readonly bool targetActors;
		readonly Func<Actor, bool> canOrder;
		IEnumerable<Actor> subjects;

		protected override MouseActionType ActionType => MouseActionType.ConfirmOrder;

		public DrTargetOrderGenerator(World world, string orderName, string cursor, bool targetActors, Func<Actor, bool> canOrder)
			: base(world)
		{
			this.orderName = orderName;
			this.cursor = cursor;
			this.targetActors = targetActors;
			this.canOrder = canOrder;
			subjects = Subjects(world, world.Selection.Actors);
		}

		IEnumerable<Actor> Subjects(World world, IEnumerable<Actor> selected)
		{
			return selected.Where(a => !a.IsDead && a.IsInWorld && a.Owner == world.LocalPlayer && canOrder(a)).ToArray();
		}

		/// <summary>True if any of the selection can take the order.</summary>
		public static bool Any(World world, Func<Actor, bool> canOrder)
		{
			return world.Selection.Actors.Any(a => !a.IsDead && a.IsInWorld && a.Owner == world.LocalPlayer && canOrder(a));
		}

		Target TargetAt(World world, CPos cell, MouseInput mi)
		{
			if (targetActors)
			{
				var actor = world.ScreenMap.ActorsAtMouse(mi).Select(a => a.Actor)
					.FirstOrDefault(a => !a.IsDead && a.Info.HasTraitInfo<ITargetableInfo>() && !world.FogObscures(a));
				if (actor != null)
					return Target.FromActor(actor);
			}

			return Target.FromCell(world, cell);
		}

		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (mi.Button != MouseButton.Left)
			{
				world.CancelInputMode();
				yield break;
			}

			var queued = mi.Modifiers.HasModifier(Modifiers.Shift);
			if (!queued)
				world.CancelInputMode();

			if (subjects.Any())
				yield return new Order(orderName, null, TargetAt(world, cell, mi), queued, null, subjects.ToArray());
		}

		public override void SelectionChanged(World world, IEnumerable<Actor> selected)
		{
			subjects = Subjects(world, selected);
			if (!subjects.Any())
				world.CancelInputMode();
		}

		public override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			return subjects.Any() ? cursor : null;
		}

		public override bool InputOverridesSelection(World world, int2 xy, MouseInput mi) => true;

		public override bool ClearSelectionOnLeftClick => false;
	}
}
