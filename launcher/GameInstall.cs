using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DarkReign.Launcher
{
	// Where the engine, the mod and the game data are, and how to start the game.
	// Two layouts: the repository (engine/bin/..., mods/dr) and a package (everything
	// flat beside DarkReign.exe, mods/ included), as packaging/windows/package.ps1 builds it.
	public sealed class GameInstall
	{
		public const string ModId = "dr";

		// The engine's own Windows launcher built for the mod: the game runs as Dark Reign,
		// with its icon, rather than as OpenRA.exe.
		const string GameHost = "DarkReignGame.exe";

		public string Root { get; private init; }
		public bool Packaged => !Directory.Exists(Path.Combine(Root, "engine"));
		public string EngineDir => Packaged ? Root : Path.Combine(Root, "engine");
		public string BinDir => Packaged ? Root : Path.Combine(EngineDir, "bin");
		public string ModsDir => Path.Combine(Root, "mods");
		public string ImportScript => Path.Combine(Root, "import-campaign.ps1");

		public string GameExe
		{
			get
			{
				var host = Path.Combine(BinDir, GameHost);
				return File.Exists(host) ? host : Path.Combine(BinDir, "OpenRA.exe");
			}
		}

		public static string SupportDir => Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenRA");

		public static string ContentDir => Path.Combine(SupportDir, "Content", ModId);
		public static string LogsDir => Path.Combine(SupportDir, "Logs");
		public static string CampaignDir => Path.Combine(SupportDir, "maps", ModId, "campaign");

		// The launcher sits at the root of the game folder; while developing, its build output
		// may sit further down, so look upwards for the folder holding mods/dr and the engine.
		public static GameInstall Locate()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var d = dir; d != null; d = d.Parent)
			{
				var root = d.FullName;
				if (Directory.Exists(Path.Combine(root, "mods", ModId))
					&& (Directory.Exists(Path.Combine(root, "engine")) || File.Exists(Path.Combine(root, "OpenRA.Game.dll"))))
					return new GameInstall { Root = root };
			}

			return new GameInstall { Root = dir.FullName };
		}

		public bool HasEngine => File.Exists(GameExe);
		public static bool HasGameData => File.Exists(Path.Combine(ContentDir, "SPRITES.FTG"));
		public static int CampaignMissions => Directory.Exists(CampaignDir) ? Directory.EnumerateDirectories(CampaignDir).Count() : 0;

		// Why the game cannot start yet, or null when it can.
		public string Problem()
		{
			if (!HasEngine)
				return Packaged
					? "The game files are incomplete. Reinstall Dark Reign."
					: "The game engine is not built yet. Run make.cmd all in the game folder.";

			if (!HasGameData)
				return "Dark Reign's game data is not installed yet. Install it from your copy of the game.";

			return null;
		}

		public Process Start(IEnumerable<string> extraArgs = null)
		{
			var info = new ProcessStartInfo(GameExe)
			{
				WorkingDirectory = BinDir,
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			info.ArgumentList.Add($"Game.Mod={ModId}");
			if (!Packaged)
				info.ArgumentList.Add("Engine.EngineDir=..");

			info.ArgumentList.Add($"Engine.ModSearchPaths={ModsDir}");

			// The engine relaunches through this path when it restarts for new settings;
			// the launcher passes those launches straight on.
			info.ArgumentList.Add($"Engine.LaunchPath={Environment.ProcessPath}");
			foreach (var a in extraArgs ?? [])
				if (!a.StartsWith("Engine.LaunchPath=", StringComparison.Ordinal))
					info.ArgumentList.Add(a);

			// Sizes in the settings are real pixels and UI Scale alone enlarges the interface,
			// rather than the engine also multiplying both by the Windows display scale.
			info.Environment["OPENRA_DISPLAY_SCALE"] = "1";

			return Process.Start(info);
		}

		// The folder holding the game's 'dark' folder, from a folder the player picked:
		// that folder, its 'dark' folder, or something inside it.
		public static string FindGameDir(string picked)
		{
			for (var d = new DirectoryInfo(picked); d != null; d = d.Parent)
			{
				if (d.Name.Equals("dark", StringComparison.OrdinalIgnoreCase) && d.Parent != null)
					return d.Parent.FullName;

				if (d.EnumerateDirectories().Any(c => c.Name.Equals("dark", StringComparison.OrdinalIgnoreCase)))
					return d.FullName;
			}

			return null;
		}

		// A copy of Dark Reign already on this PC, to start the folder picker in: GOG's install
		// (its registry entries), the usual GOG folders, or DrData beside a development checkout.
		public string FindInstalledGame()
		{
			var candidates = new List<string>();
			try
			{
				using var games = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games")
					?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GOG.com\Games");
				foreach (var id in games?.GetSubKeyNames() ?? [])
				{
					using var game = games.OpenSubKey(id);
					if (game?.GetValue("gameName") is string name && name.Contains("Dark Reign", StringComparison.OrdinalIgnoreCase)
						&& game.GetValue("path") is string path)
						candidates.Add(path);
				}
			}
			catch (Exception)
			{
				// No registry access: the folders below still apply.
			}

			var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			candidates.Add(@"C:\GOG Games\Dark Reign");
			candidates.Add(Path.Combine(programFilesX86, "GOG Galaxy", "Games", "Dark Reign"));
			candidates.Add(Path.Combine(programFilesX86, "GOG.com", "Dark Reign"));
			candidates.Add(Path.Combine(Root, "DrData"));

			return candidates.Where(Directory.Exists).Select(FindGameDir).FirstOrDefault(d => d != null);
		}

		// Runs import-campaign.ps1: copies the game data and converts the campaign. Each line
		// of its output goes to the callback; the result is its exit code.
		public async Task<int> Import(string gameDir, Action<string> output)
		{
			var shell = FindOnPath("pwsh.exe") ?? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
			var info = new ProcessStartInfo(shell)
			{
				WorkingDirectory = Root,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ImportScript, "-GameDir", gameDir })
				info.ArgumentList.Add(a);

			using var process = Process.Start(info);
			process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) output(e.Data); };
			process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) output(e.Data); };
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			await process.WaitForExitAsync();
			return process.ExitCode;
		}

		static string FindOnPath(string exe)
		{
			var path = Environment.GetEnvironmentVariable("PATH") ?? "";
			return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
				.Select(p => Path.Combine(p.Trim(), exe))
				.FirstOrDefault(File.Exists);
		}
	}
}
