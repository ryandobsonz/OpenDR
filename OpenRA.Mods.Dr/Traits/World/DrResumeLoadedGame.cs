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

using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Dr.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("A loaded game goes straight on under the original interface, as the original's did. OpenRA opens its",
		"in-game menu instead, and leaves the battlefield faded until that menu fades it in; this fades it in.")]
	public class DrResumeLoadedGameInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new DrResumeLoadedGame(); }
	}

	public class DrResumeLoadedGame : INotifyGameLoaded
	{
		void INotifyGameLoaded.GameLoaded(World world)
		{
			if (world.IsReplay)
				return;

			var fade = world.WorldActor.TraitOrDefault<MenuPostProcessEffect>();
			fade?.Fade(fade.Info.Effect, fade.Info.FadeInLength);
		}
	}
}
