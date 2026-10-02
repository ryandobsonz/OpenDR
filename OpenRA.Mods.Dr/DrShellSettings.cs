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
using System.IO;
using System.Linq;

namespace OpenRA.Mods.Dr
{
	/// <summary>
	/// The shell's own settings, which the launcher (DarkReign.exe) sets: dr-shell.yaml in the support folder.
	/// OpenRA's settings.yaml is no place for them, as the game rewrites it with its own fields alone.
	/// </summary>
	public static class DrShellSettings
	{
		static string FilePath => Path.Combine(Platform.SupportDir, "dr-shell.yaml");

		/// <summary>
		/// The 1.8.2 patch's faster turns of the cube (its fast_cubes mod, at 30 frames a second where the
		/// original's run at 10), which its launcher plays unless told not to.
		/// </summary>
		public static bool FastTransitions => Read("FastTransitions", true);

		static bool Read(string key, bool fallback)
		{
			try
			{
				if (!File.Exists(FilePath))
					return fallback;

				var node = MiniYaml.FromFile(FilePath).FirstOrDefault(n => n.Key == key);
				return node != null && bool.TryParse(node.Value.Value, out var value) ? value : fallback;
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Could not read the shell settings: {e.Message}");
				return fallback;
			}
		}
	}
}
