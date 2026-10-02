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
using System.IO;
using System.Linq;
using OpenRA.Network;

namespace OpenRA.Mods.Dr
{
	/// <summary>A saved game: its file, its name (the file's), the mission folder it was saved in (m01f ...), its map.</summary>
	public sealed record DrSavedGame(string Path, string Name, string Mission, MapPreview Map, DateTime Saved);

	/// <summary>
	/// The player's saved games (OpenRA's Saves folder for this mod and version), as the shell's Load Game and
	/// Custom Mission screens and the in-game Load/Save popup list them.
	/// </summary>
	public static class DrSavedGames
	{
		public static string Folder(ModData modData) =>
			System.IO.Path.Combine(Platform.SupportDir, "Saves", modData.Manifest.Id, modData.Manifest.Metadata.Version);

		/// <summary>The saved games, newest first: all of them, or only the campaign's or only the others'.</summary>
		public static List<DrSavedGame> List(ModData modData, bool? campaign = null)
		{
			var folder = Folder(modData);
			var saves = new List<DrSavedGame>();
			if (!Directory.Exists(folder))
				return saves;

			foreach (var path in Directory.GetFiles(folder, "*.orasav", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTime))
			{
				try
				{
					var map = modData.MapCache[new GameSave(path).GlobalSettings.Map];
					var mission = map.Status == MapStatus.Available && map.Path != null ? System.IO.Path.GetFileName(map.Path.TrimEnd('/', '\\')) : null;
					if (campaign == null || DrCampaign.IsCampaignMission(mission) == campaign)
						saves.Add(new DrSavedGame(path, System.IO.Path.GetFileNameWithoutExtension(path), mission, map, File.GetLastWriteTime(path)));
				}
				catch (Exception e)
				{
					Log.Write("debug", $"Could not read the saved game {path}: {e.Message}");
				}
			}

			return saves;
		}

		/// <summary>Starts the saved game; the shell then follows its mission as one it launched (the debrief after a win).</summary>
		public static bool Load(DrSavedGame save)
		{
			if (save.Map.Status != MapStatus.Available)
				return false;

			DrCampaign.Launched = save.Mission;
			DrCampaign.LastResult = null;
			Game.CreateAndStartLocalServer(save.Map.Uid,
			[
				Order.FromTargetString("LoadGameSave", System.IO.Path.GetFileName(save.Path), true),
				Order.Command($"state {Session.ClientState.Ready}")
			]);

			return true;
		}

		public static void Delete(DrSavedGame save)
		{
			try
			{
				File.Delete(save.Path);
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Could not delete the saved game {save.Path}: {e.Message}");
			}
		}
	}
}
