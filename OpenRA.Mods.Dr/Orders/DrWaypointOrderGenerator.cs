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
using OpenRA.Graphics;
using OpenRA.Orders;

namespace OpenRA.Mods.Dr.Orders
{
	/// <summary>
	/// The PATHS tab's Add Waypoints (the original's mouse mode 6, 0x4b73e0): each left click on the map adds a
	/// waypoint to the path being laid; a right click, or Escape, ends it. It gives no orders: Go does. Not a
	/// UnitOrderGenerator, whose clicks follow OpenRA's mouse style, and it keeps the selection.
	/// </summary>
	public class DrWaypointOrderGenerator : IOrderGenerator
	{
		readonly Action<CPos> add;
		readonly string cursor;

		public DrWaypointOrderGenerator(Action<CPos> add, string cursor)
		{
			this.add = add;
			this.cursor = cursor;
		}

		public MouseButton ActionButton => MouseButton.Left;

		public IEnumerable<Order> Order(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (mi.Button == MouseButton.Left && mi.Event == MouseInputEvent.Down && world.Map.Contains(cell))
				add(cell);
			else if (mi.Button == MouseButton.Right && mi.Event == MouseInputEvent.Up)
				world.CancelInputMode();

			return [];
		}

		public string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			return world.Map.Contains(cell) ? cursor : "generic-blocked";
		}

		public void Tick(World world) { }
		public IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		public IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }
		public IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world) { yield break; }
		public void Deactivate() { }
		public bool HandleKeyPress(KeyInput e) => false;
		public void SelectionChanged(World world, IEnumerable<Actor> selected) { }
	}
}
