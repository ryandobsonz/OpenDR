using System;
using System.IO;
using System.Linq;

namespace DarkReign.Launcher
{
	// The original menus' own settings, which the game reads from dr-shell.yaml (OpenRA.Mods.Dr's
	// DrShellSettings): OpenRA's settings.yaml keeps only the engine's fields.
	public sealed class ShellSettings
	{
		// The 1.8.2 patch's faster cube turns; its launcher plays them by default.
		public bool FastTransitions = true;

		public static string FilePath => Path.Combine(Path.GetDirectoryName(GraphicsSettings.FilePath)!, "dr-shell.yaml");

		public static ShellSettings Load()
		{
			var s = new ShellSettings();
			if (!File.Exists(FilePath))
				return s;

			foreach (var line in File.ReadAllLines(FilePath))
			{
				var colon = line.IndexOf(':');
				if (colon > 0 && line[..colon].Trim() == "FastTransitions" && bool.TryParse(line[(colon + 1)..].Trim(), out var fast))
					s.FastTransitions = fast;
			}

			return s;
		}

		public void Save()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
			File.WriteAllText(FilePath, $"FastTransitions: {(FastTransitions ? "True" : "False")}\n");
		}
	}
}
