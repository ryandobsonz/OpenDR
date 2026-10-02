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

using System.IO;
using OpenRA.Mods.Dr.FileFormats;
using OpenRA.Video;

namespace OpenRA.Mods.Dr.VideoLoaders
{
	/// <summary>Smacker videos (.smk) for OpenRA's video player: Dark Reign's intro and cutscenes.</summary>
	public class SmackerLoader : IVideoLoader
	{
		public bool TryParseVideo(Stream s, bool useFramePadding, out IVideo video)
		{
			video = null;
			if (s.Length < 104)
				return false;

			var start = s.Position;
			var signature = s.ReadASCII(4);
			s.Position = start;
			if (signature != "SMK2" && signature != "SMK4")
				return false;

			video = new SmackerVideo(s, useFramePadding);
			return true;
		}
	}
}
