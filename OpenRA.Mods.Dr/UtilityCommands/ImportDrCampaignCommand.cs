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
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenRA.FileSystem;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Dr.FileFormats;
using OpenRA.Primitives;

namespace OpenRA.Mods.Dr.UtilityCommands
{
	/// <summary>
	/// Converts the original campaign missions into mission maps that carry their own scenario,
	/// trigger, end-condition and AI files, which <c>DrScenarioScript</c> runs in game.
	/// The output is game data, so it belongs in the user's map folder, never the repository.
	/// </summary>
	class ImportDrCampaignCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--import-dr-campaign";

		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length >= 3;

		[Desc("DARKDIR", "OUTDIR", "[MISSION ...]", "Convert the campaign missions under DARKDIR/scenario/FIXED (DARKDIR is the game's 'dark' folder) into mission maps in OUTDIR.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// HACK: The engine code assumes that Game.modData is set.
			var modData = Game.ModData = utility.ModData;

			var darkDir = args[1];
			var outDir = args[2];
			var fixedDir = Resolve(darkDir, "scenario", "fixed") ?? throw new DirectoryNotFoundException($"No scenario/FIXED under {darkDir}");
			var aipDir = Resolve(darkDir, "aip");
			var strings = LoadStrings(darkDir);

			var missions = args.Length > 3
				? args.Skip(3).Select(m => Resolve(fixedDir, m) ?? throw new DirectoryNotFoundException(m))
				: Directory.GetDirectories(fixedDir);

			Directory.CreateDirectory(outDir);
			foreach (var missionDir in missions)
			{
				try
				{
					Convert(modData, missionDir, aipDir, strings, outDir);
				}
				catch (Exception e)
				{
					Console.WriteLine($"{Path.GetFileName(missionDir)}: FAILED: {e}");
				}
			}
		}

		static void Convert(ModData modData, string missionDir, string aipDir, Dictionary<string, string> strings, string outDir)
		{
			var name = Path.GetFileName(missionDir).ToLowerInvariant();
			var scnPath = Resolve(missionDir, name + ".scn") ?? throw new FileNotFoundException(name + ".scn");
			var mapPath = Resolve(missionDir, name + ".map") ?? throw new FileNotFoundException(name + ".map");
			var warnings = new SortedSet<string>();

			DrScenario scenario;
			using (var s = File.OpenRead(scnPath))
				scenario = new DrScenario(s);

			// The campaign needs the full game, which has the snow art the demo lacks.
			var tileset = scenario.Terrain switch { "ALIEN" => "AURALIEN", var t => t };
			if (!modData.DefaultTerrainInfo.TryGetValue(tileset, out var terrainInfo))
				throw new InvalidDataException($"Unknown tileset {tileset}");

			Map map;
			using (var stream = File.OpenRead(mapPath))
			{
				if (stream.ReadASCII(4) != "MAP_")
					throw new InvalidDataException("Map file did not start with MAP_");

				stream.ReadInt32(); // Version
				var width = stream.ReadInt32();
				var height = stream.ReadInt32();
				stream.ReadInt32(); // Tileset number; the scenario's SetDefaultTerrain is the one used
				map = ImportDrMapCommand.LoadTerrain(modData, stream, terrainInfo, width, height);
			}

			map.Title = MissionTitle(name, strings);
			map.Author = "Auran (converted by OpenDR)";
			map.Visibility = MapVisibility.MissionSelector;
			map.Categories = ImmutableArray.Create("Campaign");

			var players = CreatePlayers(scenario);
			map.PlayerDefinitions = players.ToMiniYaml();

			var actors = new List<MiniYamlNode>();
			foreach (var p in scenario.Placements)
			{
				var cell = DrScenario.TileToCell(p.X, p.Y);
				if (p.X < 0 || p.Y < 0 || !map.Tiles.Contains(cell))
					continue;

				if (p.IsBuilding && (p.Type.Equals("impww", StringComparison.OrdinalIgnoreCase) || p.Type.Equals("impmn", StringComparison.OrdinalIgnoreCase)))
				{
					// Water wells and taelon mines are resources here, not buildings.
					var type = p.Type.Equals("impww", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
					var resourceCell = DrScenario.TileToCell(p.X + 1, p.Y + 1);
					if (map.Tiles.Contains(resourceCell))
						map.Resources[resourceCell] = new ResourceTile((byte)type, 255);
					continue;
				}

				string actorType, actorName, owner;
				if (p.IsThing)
				{
					if (!ImportDrMapCommand.ThingNames.TryGetValue(p.Type, out actorType))
					{
						if (!ImportDrMapCommand.KnownUnknownThings.Contains(p.Type))
							warnings.Add("thing " + p.Type);
						continue;
					}

					actorName = "t" + p.Id;
					owner = "Neutral";
				}
				else
				{
					var table = p.IsBuilding ? ImportDrMapCommand.BuildingNames : ImportDrMapCommand.UnitNames;
					if (!table.TryGetValue(p.Type, out actorType))
					{
						if (!ImportDrMapCommand.KnownUnknownBuildings.Contains(p.Type))
							warnings.Add((p.IsBuilding ? "building " : "unit ") + p.Type);
						continue;
					}

					actorName = ActorName(p.Id);
					owner = TeamName(p.Team);
				}

				var reference = new ActorReference(actorType.ToLowerInvariant())
				{
					new LocationInit(cell),
					new OwnerInit(owner)
				};

				if (p.Health < 100)
					reference.Add(new HealthInit(p.Health));

				actors.Add(new MiniYamlNode(actorName, reference.Save()));
			}

			map.ActorDefinitions = actors;
			map.RuleDefinitions = new MiniYaml("dr|rules/campaign-maprules.yaml, dr|rules/campaign-tooltips.yaml, dr|rules/campaign-dr.yaml, rules.yaml");

			var target = Path.Combine(outDir, name);
			if (Directory.Exists(target))
				Directory.Delete(target, true);
			Directory.CreateDirectory(target);
			map.Save(new Folder(target));

			// The original mission files travel with the map, read by DrScenarioScript at run time.
			var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			void Copy(string path)
			{
				var file = Path.GetFileName(path).ToLowerInvariant();
				if (copied.Add(file))
					File.Copy(path, Path.Combine(target, file), true);
			}

			foreach (var f in Directory.GetFiles(missionDir))
			{
				var ext = Path.GetExtension(f).ToLowerInvariant();
				if (ext is ".scn" or ".fsm" or ".end" or ".aip" or ".brf")
					Copy(f);
			}

			// Teams without their own FSM get the default for their side and difficulty, from the shared AIP folder.
			foreach (var team in scenario.Teams.Values)
			{
				if (team.FsmFiles.Length > 0 || team.Index == 0)
					continue;

				for (var v = 0; v < 3; v++)
				{
					var def = Resolve(missionDir, $"def_{team.Side:00}_{v}.fsm") ?? (aipDir != null ? Resolve(aipDir, $"def_{team.Side:00}_{v}.fsm") : null);
					if (def != null)
						Copy(def);
				}
			}

			// AIPs named by the FSMs come from the mission, then the shared folder; repeat until nothing new appears.
			var messageKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
			for (var pass = 0; pass < 4; pass++)
			{
				foreach (var f in Directory.GetFiles(target).Where(f => f.EndsWith(".fsm", StringComparison.Ordinal) || f.EndsWith(".end", StringComparison.Ordinal)).ToList())
				{
					var text = File.ReadAllText(f, Encoding.Latin1);
					foreach (Match m in Regex.Matches(text, @"SetAIPFile\s*\(\s*([\w.]+)", RegexOptions.IgnoreCase))
					{
						var aip = Resolve(missionDir, m.Groups[1].Value) ?? (aipDir != null ? Resolve(aipDir, m.Groups[1].Value) : null);
						if (aip != null)
							Copy(aip);
						else
							warnings.Add("aip " + m.Groups[1].Value);
					}

					foreach (Match m in Regex.Matches(text, @"TriggerMessage\s*\(\s*""?(\w+)", RegexOptions.IgnoreCase))
						messageKeys.Add(m.Groups[1].Value);
				}
			}

			// Messages the triggers show, keyed as in the original string table.
			var messages = new StringBuilder();
			foreach (var key in messageKeys)
			{
				if (strings.TryGetValue(key, out var text))
					messages.Append(key).Append('\t').Append(text).Append('\n');
				else
					warnings.Add("message " + key);
			}

			File.WriteAllText(Path.Combine(target, "messages.txt"), messages.ToString());
			File.WriteAllText(Path.Combine(target, "rules.yaml"), RulesYaml(missionDir, name));

			Console.WriteLine($"{name}: {map.Title}, {actors.Count} actors" + (warnings.Count > 0 ? "; skipped " + string.Join(", ", warnings) : ""));
		}

		public static string ActorName(int id) => "u" + id.ToString(CultureInfo.InvariantCulture);

		public static string TeamName(int team) => "Team" + team.ToString(CultureInfo.InvariantCulture);

		static readonly Color[] TeamColors =
		{
			Color.FromArgb(28, 115, 255), Color.FromArgb(254, 17, 0), Color.FromArgb(80, 200, 80), Color.FromArgb(240, 200, 40),
			Color.FromArgb(160, 160, 160), Color.FromArgb(200, 120, 240), Color.FromArgb(240, 140, 40), Color.FromArgb(60, 220, 220),
			Color.FromArgb(180, 160, 120)
		};

		public static string SideFaction(int side) => side switch
		{
			1 or 5 => "imperium",
			3 => "togran",
			_ => "fguard",
		};

		static MapPlayers CreatePlayers(DrScenario scenario)
		{
			var players = new MapPlayers();
			players.Players["Neutral"] = new PlayerReference
			{
				Name = "Neutral",
				OwnsWorld = true,
				NonCombatant = true,
				Faction = "fguard",
				Color = Color.FromArgb(255, 255, 255)
			};

			var teams = Enumerable.Range(0, 9).Select(i => scenario.Teams.TryGetValue(i, out var t) ? t : new DrTeam { Index = i, Side = 2 }).ToList();
			var hasActors = scenario.Placements.Where(p => !p.IsThing).Select(p => p.Team).ToHashSet();

			foreach (var team in teams)
			{
				var pr = new PlayerReference
				{
					Name = TeamName(team.Index),
					Faction = SideFaction(team.Side),
					Color = TeamColors[team.Index],
					LockFaction = true,
					LockColor = true,
					Allies = teams.Where(t => t != team && team.Alliance[t.Index] == 2).Select(t => TeamName(t.Index)).ToImmutableArray(),
					Enemies = teams.Where(t => t != team && team.Alliance[t.Index] == 0).Select(t => TeamName(t.Index)).ToImmutableArray(),
				};

				if (team.Index == 0)
				{
					pr.Playable = true;
					pr.Required = true;
					pr.AllowBots = false;
					pr.LockSpawn = true;
					pr.LockTeam = true;
				}
				else if (hasActors.Contains(team.Index) && team.Side != 2)
					pr.Bot = "dr-campaign";

				players.Players[pr.Name] = pr;
			}

			return players;
		}

		static string MissionTitle(string name, Dictionary<string, string> strings)
		{
			var m = Regex.Match(name, @"^m(\d\d)([fi])$");
			if (m.Success && strings.TryGetValue("SS_MISSION_NAME_" + m.Groups[1].Value, out var title))
				return $"{int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)}: {TitleCase(title)}";

			m = Regex.Match(name, @"^(fgx|sh)(\d)$");
			if (m.Success)
			{
				var key = (m.Groups[1].Value == "sh" ? "SS_MISSION_SHADOWHAND_0" : "SS_MISSION_XENITE_0") + (int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) - 1);
				if (strings.TryGetValue(key, out var expTitle))
					return expTitle;
			}

			return name switch
			{
				"m13t" => "13: The Togran",
				_ => name.ToUpperInvariant()
			};
		}

		static string TitleCase(string s)
		{
			return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()).Replace(" On ", " on ").Replace(" Of ", " of ");
		}

		/// <summary>The mission's rules: its briefing, and the scenario runtime.</summary>
		static string RulesYaml(string missionDir, string name)
		{
			var briefing = "";
			var brf = Resolve(missionDir, name + ".brf");
			if (brf != null)
			{
				var sections = BriefingSections(File.ReadAllText(brf, Encoding.Latin1));
				briefing = string.Join("\\n\\n", new[] { sections.GetValueOrDefault(0), sections.GetValueOrDefault(1) }.Where(t => !string.IsNullOrEmpty(t)));
			}

			// MiniYaml takes '#' as a comment.
			briefing = briefing.Replace("#", "\\#");

			return "World:\n" +
				"\tMissionData:\n" +
				$"\t\tBriefing: {briefing}\n" +
				"\tDrScenarioScript:\n";
		}

		/// <summary>
		/// A briefing file's numbered sections: \0 the setting, \1 the orders, \2 the historical outcome
		/// shown on victory, \3 Togra's closing line. In the text, \n breaks, \c centres and \s is a space.
		/// </summary>
		public static Dictionary<int, string> BriefingSections(string text)
		{
			var sections = new Dictionary<int, string>();
			foreach (Match m in Regex.Matches(text, @"\\(\d)\s*(.*?)(?=\\\d|$)", RegexOptions.Singleline))
			{
				var body = Regex.Replace(m.Groups[2].Value, @"\s*\r?\n\s*", " ");
				body = body.Replace("\\c", "").Replace("\\s", " ");
				body = Regex.Replace(body, @"(\s*\\n\s*)+", mm => Regex.Matches(mm.Value, @"\\n").Count > 1 ? "\\n\\n" : "\\n");
				sections[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = Regex.Replace(body, @" {2,}", " ").Trim();
			}

			return sections;
		}

		static Dictionary<string, string> LoadStrings(string darkDir)
		{
			var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var local = Resolve(darkDir, "local");
			if (local == null)
				return strings;

			foreach (var file in new[] { "mlstring.cfg", "mlstrnex.cfg" })
			{
				var path = Resolve(local, file);
				if (path == null)
					continue;

				foreach (Match m in Regex.Matches(File.ReadAllText(path, Encoding.Latin1), @"^#define\s+(\w+)\s+""(.*)""", RegexOptions.Multiline))
					strings[m.Groups[1].Value] = m.Groups[2].Value;
			}

			return strings;
		}

		/// <summary>Finds a path under a directory ignoring case, as the game's files mix it freely.</summary>
		static string Resolve(string dir, params string[] parts)
		{
			var current = dir;
			foreach (var part in parts)
			{
				if (!Directory.Exists(current))
					return null;

				var match = Directory.GetFileSystemEntries(current).FirstOrDefault(e => Path.GetFileName(e).Equals(part, StringComparison.OrdinalIgnoreCase));
				if (match == null)
					return null;

				current = match;
			}

			return current;
		}
	}
}
