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

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Attach this to the world actor. Each resource cell is a spring (the original's impww and impmn):",
		"it starts with an amount and regrows to its MaxDensity, as the original's SetResource gives them.")]
	public class DrResourceLayerInfo : ResourceLayerInfo
	{
		[Desc("Bales a spring of each resource type starts with. Types not listed start full.")]
		public readonly Dictionary<string, int> InitialDensity = new();

		[Desc("Ticks for a spring of each resource type to regrow a bale. Types not listed never regrow.")]
		public readonly Dictionary<string, int> RegrowTicks = new();

		public override object Create(ActorInitializer init) { return new DrResourceLayer(init.Self, this); }
	}

	public class DrResourceLayer : ResourceLayer, ITick
	{
		readonly DrResourceLayerInfo info;
		readonly List<(CPos Cell, string Type, int Interval)> springs = new();
		int ticks;

		public DrResourceLayer(Actor self, DrResourceLayerInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		protected override void WorldLoaded(World w, WorldRenderer wr)
		{
			base.WorldLoaded(w, wr);

			foreach (var cell in w.Map.AllCells)
			{
				var content = Content[cell];
				if (content.Type == null)
					continue;

				if (info.InitialDensity.TryGetValue(content.Type, out var initial))
					Content[cell] = new ResourceLayerContents(content.Type, (byte)initial.Clamp(1, info.ResourceTypes[content.Type].MaxDensity));

				if (info.RegrowTicks.TryGetValue(content.Type, out var interval) && interval > 0)
					springs.Add((cell, content.Type, interval));
			}
		}

		void ITick.Tick(Actor self)
		{
			ticks++;
			IResourceLayer layer = this;
			foreach (var (cell, type, interval) in springs)
				if (ticks % interval == 0 && layer.CanAddResource(type, cell, 1))
					layer.AddResource(type, cell, 1);
		}
	}
}
