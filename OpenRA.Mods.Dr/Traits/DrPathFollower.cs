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
using System.Globalization;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	/// <summary>The original's path directions (TRAILMDE.BMP): to the end, back and forth, round and round.</summary>
	public enum DrPathMode { OneWay, PingPong, Loop }

	[Desc("Follows a path of waypoints laid on the PATHS tab: the DrFollowPath order.")]
	public class DrPathFollowerInfo : TraitInfo, Requires<IMoveInfo>
	{
		public override object Create(ActorInitializer init) { return new DrPathFollower(); }
	}

	public class DrPathFollower : IResolveOrder
	{
		public const string OrderName = "DrFollowPath";

		/// <summary>The order's TargetString: the mode, then each waypoint's cell, "1|10,12|14,20".</summary>
		public static string Encode(DrPathMode mode, CPos[] points)
		{
			return string.Join("|", points.Select(p => p.X.ToString(CultureInfo.InvariantCulture) + "," + p.Y.ToString(CultureInfo.InvariantCulture))
				.Prepend(((int)mode).ToString(CultureInfo.InvariantCulture)));
		}

		static bool TryDecode(string text, out DrPathMode mode, out CPos[] points)
		{
			mode = DrPathMode.OneWay;
			points = [];
			var parts = text?.Split('|');
			if (parts == null || parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) || m < 0 || m > 2)
				return false;

			mode = (DrPathMode)m;
			var cells = new CPos[parts.Length - 1];
			for (var i = 1; i < parts.Length; i++)
			{
				var xy = parts[i].Split(',');
				if (xy.Length != 2 || !int.TryParse(xy[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
					|| !int.TryParse(xy[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
					return false;

				cells[i - 1] = new CPos(x, y);
			}

			points = cells;
			return true;
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != OrderName || !TryDecode(order.TargetString, out var mode, out var points))
				return;

			var map = self.World.Map;
			points = points.Where(map.Contains).ToArray();
			if (points.Length > 0)
				self.QueueActivity(order.Queued, new DrFollowPath(self, points, mode));
		}
	}

	/// <summary>Walks a path's waypoints in turn: to the last and stop, back and forth, or round again.</summary>
	public class DrFollowPath : Activity
	{
		readonly IMove move;
		readonly CPos[] points;
		readonly DrPathMode mode;
		int next;
		int step = 1;
		CPos? lapStart;

		public DrFollowPath(Actor self, CPos[] points, DrPathMode mode)
		{
			move = self.Trait<IMove>();
			this.points = points;
			this.mode = mode;
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			if (next < 0 || next >= points.Length)
			{
				if (mode == DrPathMode.OneWay || points.Length < 2)
					return true;

				// A lap that got nowhere (every waypoint out of reach): stop rather than search again every tick.
				if (lapStart == self.Location)
					return true;

				lapStart = self.Location;

				if (mode == DrPathMode.Loop)
					next = 0;
				else
				{
					step = -step;
					next += 2 * step;
				}
			}

			QueueChild(move.MoveTo(points[next], 2));
			next += step;
			return false;
		}

		public override System.Collections.Generic.IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			// The waypoints still ahead, as OpenRA draws a unit's queued moves.
			var count = mode == DrPathMode.OneWay ? points.Length - Math.Clamp(next - 1, 0, points.Length) : points.Length;
			for (var i = 0; i < count; i++)
			{
				var index = mode == DrPathMode.OneWay ? Math.Clamp(next - 1, 0, points.Length - 1) + i : i;
				yield return new TargetLineNode(Target.FromCell(self.World, points[index]), Color.Green);
			}
		}
	}
}
