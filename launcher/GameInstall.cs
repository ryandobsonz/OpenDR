using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DarkReign.Launcher
{
	// Where the engine, the mod and the game data are, and how to start the game:
	// what launch-game.cmd does, without the console window.
	public sealed class GameInstall
	{
		public const string ModId = "dr";

		public string Root { get; private init; }
		public string EngineDir => Path.Combine(Root, "engine");
		public string GameExe => Path.Combine(EngineDir, "bin", "OpenRA.exe");
		public string ModsDir => Path.Combine(Root, "mods");

		public static string SupportDir => Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenRA");

		public static string ContentDir => Path.Combine(SupportDir, "Content", ModId);
		public static string LogsDir => Path.Combine(SupportDir, "Logs");
		public static string CampaignDir => Path.Combine(SupportDir, "maps", ModId, "campaign");

		// The launcher sits at the root of the game folder; while developing, its build output
		// may sit further down, so look upwards for the folder holding engine/ and mods/dr.
		public static GameInstall Locate()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var d = dir; d != null; d = d.Parent)
				if (Directory.Exists(Path.Combine(d.FullName, "mods", ModId)) && Directory.Exists(Path.Combine(d.FullName, "engine")))
					return new GameInstall { Root = d.FullName };

			return new GameInstall { Root = dir.FullName };
		}

		// Why the game cannot start yet, or null when it can.
		public string Problem()
		{
			if (!File.Exists(GameExe))
				return "The game engine is not built yet. Run make.cmd all in the game folder.";

			if (!File.Exists(Path.Combine(ContentDir, "SPRITES.FTG")))
				return "The Dark Reign game data is not installed. Run import-campaign.ps1 with your Dark Reign folder (see CAMPAIGN.md).";

			return null;
		}

		public bool HasCampaign => Directory.Exists(CampaignDir) && Directory.EnumerateDirectories(CampaignDir).Any();

		public Process Start(IEnumerable<string> extraArgs = null)
		{
			var info = new ProcessStartInfo(GameExe)
			{
				WorkingDirectory = EngineDir,
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			info.ArgumentList.Add($"Game.Mod={ModId}");
			info.ArgumentList.Add("Engine.EngineDir=..");
			info.ArgumentList.Add($"Engine.ModSearchPaths={ModsDir}");

			// The engine relaunches through this path when it switches mods; the launcher passes those straight on.
			info.ArgumentList.Add($"Engine.LaunchPath={Environment.ProcessPath}");
			foreach (var a in extraArgs ?? [])
				info.ArgumentList.Add(a);

			// Sizes in the settings are real pixels and UI Scale alone enlarges the interface,
			// rather than the engine also multiplying both by the Windows display scale.
			info.Environment["OPENRA_DISPLAY_SCALE"] = "1";

			return Process.Start(info);
		}
	}
}
