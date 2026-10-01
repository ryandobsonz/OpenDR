using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DarkReign.Launcher
{
	public enum WindowMode { Fullscreen, PseudoFullscreen, Windowed }

	// The Graphics section of OpenRA's settings.yaml, the part the launcher edits. The file stays
	// the single source of truth: what the game's own Display menu saves, the launcher shows.
	// Defaults are the engine's (OpenRA.Game/Settings.cs, GraphicSettings).
	public sealed class GraphicsSettings
	{
		public WindowMode Mode = WindowMode.PseudoFullscreen;
		public int FullscreenWidth, FullscreenHeight;
		public int WindowedWidth = 1024, WindowedHeight = 768;
		public int VideoDisplay;
		public float UIScale = 1;
		public bool HasUIScale;
		public string ViewportDistance = "Medium";
		public bool VSync = true;

		static readonly string[] Owned = ["Mode", "FullscreenSize", "WindowedSize", "VideoDisplay", "UIScale", "ViewportDistance", "VSync"];

		public static string FilePath { get; set; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenRA", "settings.yaml");

		public static GraphicsSettings Load()
		{
			var s = new GraphicsSettings();
			if (!File.Exists(FilePath))
				return s;

			var lines = File.ReadAllLines(FilePath);
			var (start, end) = FindSection(lines);
			for (var i = start + 1; i < end; i++)
			{
				var (key, value) = Split(lines[i]);
				switch (key)
				{
					case "Mode": Enum.TryParse(value, true, out s.Mode); break;
					case "FullscreenSize": TryParseSize(value, ref s.FullscreenWidth, ref s.FullscreenHeight); break;
					case "WindowedSize": TryParseSize(value, ref s.WindowedWidth, ref s.WindowedHeight); break;
					case "VideoDisplay": int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.VideoDisplay); break;
					case "UIScale": s.HasUIScale = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out s.UIScale); break;
					case "ViewportDistance": s.ViewportDistance = value; break;
					case "VSync": bool.TryParse(value, out s.VSync); break;
				}
			}

			if (!s.HasUIScale)
				s.UIScale = 1;

			return s;
		}

		public void Save()
		{
			var values = new Dictionary<string, string>
			{
				["Mode"] = Mode.ToString(),
				["FullscreenSize"] = $"{FullscreenWidth},{FullscreenHeight}",
				["WindowedSize"] = $"{WindowedWidth},{WindowedHeight}",
				["VideoDisplay"] = VideoDisplay.ToString(CultureInfo.InvariantCulture),
				["UIScale"] = UIScale.ToString(CultureInfo.InvariantCulture),
				["ViewportDistance"] = ViewportDistance,
				["VSync"] = VSync ? "True" : "False",
			};

			var text = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
			var newline = text.Contains("\r\n") || text.Length == 0 ? "\r\n" : "\n";
			var lines = text.Length == 0 ? new List<string>() : text.Replace("\r\n", "\n").Split('\n').ToList();
			if (lines.Count > 0 && lines[^1].Length == 0)
				lines.RemoveAt(lines.Count - 1);

			var (start, end) = FindSection(lines.ToArray());
			if (start < 0)
			{
				// No Graphics section yet: OpenRA writes it first.
				var section = new List<string> { "Graphics:" };
				section.AddRange(Owned.Select(k => $"\t{k}: {values[k]}"));
				section.Add("");
				lines.InsertRange(0, section);
			}
			else
			{
				var written = new HashSet<string>();
				for (var i = start + 1; i < end; i++)
				{
					var (key, _) = Split(lines[i]);
					if (key != null && values.TryGetValue(key, out var value))
					{
						lines[i] = $"\t{key}: {value}";
						written.Add(key);
					}
				}

				lines.InsertRange(end, Owned.Where(k => !written.Contains(k)).Select(k => $"\t{k}: {values[k]}"));
			}

			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			var output = string.Join(newline, lines) + newline;
			var temp = FilePath + ".launcher";
			File.WriteAllText(temp, output, new UTF8Encoding(false));
			File.Move(temp, FilePath, true);
		}

		// The top-level "Graphics:" line, and the index just past its indented block
		// (excluding trailing blank lines, so new keys go before the section's separator).
		static (int Start, int End) FindSection(string[] lines)
		{
			var start = Array.FindIndex(lines, l => l.TrimEnd() == "Graphics:");
			if (start < 0)
				return (-1, -1);

			var end = start + 1;
			while (end < lines.Length && lines[end].Length > 0 && char.IsWhiteSpace(lines[end][0]))
				end++;

			return (start, end);
		}

		static (string Key, string Value) Split(string line)
		{
			// Only direct children of the section: one tab of indent.
			if (line.Length < 2 || line[0] != '\t' || char.IsWhiteSpace(line[1]))
				return (null, null);

			var colon = line.IndexOf(':');
			if (colon < 0)
				return (null, null);

			return (line[1..colon].Trim(), line[(colon + 1)..].Trim());
		}

		static void TryParseSize(string value, ref int width, ref int height)
		{
			var parts = value.Split(',');
			if (parts.Length == 2
				&& int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
				&& int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
			{
				width = w;
				height = h;
			}
		}
	}
}
