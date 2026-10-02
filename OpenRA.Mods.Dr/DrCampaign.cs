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

namespace OpenRA.Mods.Dr
{
	/// <summary>
	/// The original campaign's progress, as the shell's mission ring shows it: twelve missions, each playable
	/// from either side once the one before it is won, then the Togran's. Kept in dr-campaign.yaml in the
	/// support folder. Missions are named as the importer names their maps: m01f, m01i ... m13t, t1 ... t4.
	/// </summary>
	public static class DrCampaign
	{
		public const int Missions = 12;
		public const int Togran = 13;

		static HashSet<string> won;

		/// <summary>The mission the shell last launched, and whether it was won, for the shell to come back to.</summary>
		public static string Launched;
		public static bool? LastResult;

		/// <summary>
		/// The debrief's rows, as the original shows them: the player's team and team 1, each its side (the
		/// scenario's: 0 Freedom Guard, 1 Imperium, 2 civilian, 3 Togran; -1 for no such team) and figures.
		/// </summary>
		public static (int Side, int[] Figures)[] LastStatistics;

		static string FilePath => Path.Combine(Platform.SupportDir, "dr-campaign.yaml");

		public static string MissionName(int number, char side) =>
			number == Togran ? "m13t" : $"m{number:D2}{side}";

		static HashSet<string> Won
		{
			get
			{
				if (won != null)
					return won;

				won = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				try
				{
					if (File.Exists(FilePath))
					{
						var node = MiniYaml.FromFile(FilePath).FirstOrDefault(n => n.Key == "Won");
						if (node != null && !string.IsNullOrEmpty(node.Value.Value))
							foreach (var m in node.Value.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
								won.Add(m);
					}
				}
				catch (Exception e)
				{
					Log.Write("debug", $"Could not read the campaign progress: {e.Message}");
				}

				return won;
			}
		}

		public static bool HasWon(string mission) => Won.Contains(mission);

		public static bool HasWon(int number) =>
			number == Togran ? HasWon("m13t") : HasWon(MissionName(number, 'f')) || HasWon(MissionName(number, 'i'));

		/// <summary>Mission one is always open; each after it once the one before is won, and the Togran's once all twelve are.</summary>
		public static bool IsUnlocked(int number) =>
			number <= 1 || (number == Togran ? Enumerable.Range(1, Missions).All(HasWon) : HasWon(number - 1));

		public static int Completed => Enumerable.Range(1, Missions).Count(HasWon);

		public static bool HasProgress => Won.Count > 0;

		public static void RecordResult(string mission, bool victory)
		{
			LastResult = victory;
			if (!victory || !Won.Add(mission))
				return;

			Save();
		}

		public static void Reset()
		{
			Won.Clear();
			Save();
		}

		static void Save()
		{
			// Scripted test runs (tools/campaign/run-game.ps1) leave the player's progress alone.
			if (Environment.GetEnvironmentVariable("OPENDR_SCRIPTED") != null)
				return;

			try
			{
				var nodes = new List<MiniYamlNode> { new("Won", string.Join(", ", Won.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))) };
				nodes.WriteToFile(FilePath);
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Could not save the campaign progress: {e.Message}");
			}
		}
	}
}
