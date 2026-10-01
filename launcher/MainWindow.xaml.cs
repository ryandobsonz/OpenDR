using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using System.Windows.Media.Imaging;

namespace DarkReign.Launcher
{
	public partial class MainWindow : Window
	{
		// The smallest interface the game lays out (WorldViewportSizes.MinEffectiveResolution),
		// and the interface sizes its own Display menu offers.
		const int MinUIWidth = 1024, MinUIHeight = 720;
		static readonly float[] UIScales = [1f, 1.25f, 1.5f, 1.75f, 2f];

		static readonly Resolution[] WindowSizes =
		[
			new(1024, 768), new(1280, 720), new(1280, 800), new(1280, 960), new(1366, 768), new(1440, 900),
			new(1600, 900), new(1600, 1200), new(1680, 1050), new(1920, 1080), new(1920, 1200), new(2560, 1080),
			new(2560, 1440), new(2560, 1600), new(3440, 1440), new(3840, 1600), new(3840, 2160),
		];

		static readonly (string Value, string Label, string Hint)[] Zooms =
		[
			("Close", "Close", "Shows about as much of the battlefield as the original 640 × 480 game."),
			("Medium", "Medium", "Shows a little more of the battlefield than the original."),
			("Far", "Far", "Shows much more of the battlefield; units are smaller."),
			("Native", "Furthest", "No scaling: one game pixel per screen pixel."),
		];

		sealed record ResolutionItem(Resolution Size, string Label)
		{
			public override string ToString() => Label;
		}

		readonly GameInstall install = GameInstall.Locate();
		readonly List<Display> displays;
		GraphicsSettings settings;
		bool updating;

		public MainWindow()
		{
			InitializeComponent();

			displays = Displays.Enumerate();
			if (displays.Count == 0)
				displays.Add(new Display { Name = "Display", Primary = true, Native = new Resolution(1920, 1080), Scale = 1, Modes = [new(1920, 1080)] });

			settings = GraphicsSettings.Load();
			if (!settings.HasUIScale)
				settings.UIScale = SuggestedScale(CurrentDisplay, CurrentDisplay.Native);

			LoadArt();
			VersionText.Text = Version();
			BuildOptionRows();
			ShowSettings();
			ShowProblem();
		}

		Display CurrentDisplay => displays.FirstOrDefault(d => d.Index == settings.VideoDisplay) ?? displays[0];

		// Art from the original 1.8.2 launcher, which import-campaign.ps1 copies with the game data.
		void LoadArt()
		{
			var art = Path.Combine(GameInstall.ContentDir, "launcher");
			var backdrop = LoadImage(Path.Combine(art, "bg-dkreign.png"));
			var logo = LoadImage(Path.Combine(art, "logo-dkreign.png"));
			if (backdrop != null)
				Backdrop.Source = backdrop;

			if (logo != null)
			{
				Logo.Source = logo;
				TextLogo.Visibility = Visibility.Collapsed;
			}
		}

		static BitmapImage LoadImage(string path)
		{
			if (!File.Exists(path))
				return null;

			try
			{
				var image = new BitmapImage();
				image.BeginInit();
				image.CacheOption = BitmapCacheOption.OnLoad;
				image.UriSource = new Uri(path);
				image.EndInit();
				image.Freeze();
				return image;
			}
			catch (Exception)
			{
				return null;
			}
		}

		string Version()
		{
			var version = Assembly.GetExecutingAssembly().GetName().Version;
			var engine = "";
			try
			{
				// The engine's VERSION file, in engine/ or beside a packaged launcher.
				var line = File.ReadLines(Path.Combine(install.EngineDir, "VERSION")).FirstOrDefault()?.Trim();
				if (!string.IsNullOrEmpty(line))
					engine = "  ·  OpenRA " + line;
			}
			catch (Exception)
			{
			}

			return $"v{version.Major}.{version.Minor}.{version.Build}{engine}";
		}

		void BuildOptionRows()
		{
			foreach (var scale in UIScales)
			{
				var button = new RadioButton
				{
					Style = (Style)FindResource("Segment"),
					GroupName = "Scale",
					Content = $"{scale * 100:0}%",
					Tag = scale,
				};
				button.Checked += (_, _) => { if (!updating) { settings.UIScale = (float)button.Tag; ShowSummary(); } };
				ScaleRow.Children.Add(button);
			}

			foreach (var zoom in Zooms)
			{
				var button = new RadioButton
				{
					Style = (Style)FindResource("Segment"),
					GroupName = "Zoom",
					Content = zoom.Label,
					Tag = zoom.Value,
				};
				button.Checked += (_, _) => { if (!updating) { settings.ViewportDistance = (string)button.Tag; ShowZoomHint(); } };
				ZoomRow.Children.Add(button);
			}
		}

		// Puts the settings into the controls, correcting any the chosen display cannot do.
		void ShowSettings()
		{
			updating = true;
			try
			{
				var display = CurrentDisplay;
				settings.VideoDisplay = display.Index;

				ModeFullscreen.IsChecked = settings.Mode == WindowMode.Fullscreen;
				ModeBorderless.IsChecked = settings.Mode == WindowMode.PseudoFullscreen;
				ModeWindowed.IsChecked = settings.Mode == WindowMode.Windowed;
				ModeHint.Text = settings.Mode switch
				{
					WindowMode.Fullscreen => "Takes over the monitor at the chosen resolution. Switching away with Alt+Tab is slower.",
					WindowMode.Windowed => "A window on the desktop, centred on the chosen monitor.",
					_ => "Fills the monitor at its desktop resolution, with instant Alt+Tab. Recommended.",
				};

				if (MonitorBox.ItemsSource != displays)
					MonitorBox.ItemsSource = displays;

				MonitorBox.SelectedItem = display;
				MonitorBox.IsEnabled = displays.Count > 1;

				var (items, selected) = ResolutionChoices(display);
				// Replacing the list raises SelectionChanged later, outside this guard; only do it when it differs.
				if (ResolutionBox.ItemsSource is not List<ResolutionItem> shown || !shown.SequenceEqual(items))
					ResolutionBox.ItemsSource = items;

				ResolutionBox.SelectedItem = selected;
				ResolutionBox.IsEnabled = settings.Mode != WindowMode.PseudoFullscreen && items.Count > 1;
				Apply(selected.Size);

				var size = selected.Size;
				var maxScale = Math.Min(size.Width / (float)MinUIWidth, size.Height / (float)MinUIHeight);
				var allowed = UIScales.Where(s => s <= maxScale + 0.001f).DefaultIfEmpty(1f).ToArray();
				if (!allowed.Contains(settings.UIScale))
					settings.UIScale = allowed.Where(s => s <= settings.UIScale).DefaultIfEmpty(1f).Max();

				foreach (RadioButton button in ScaleRow.Children)
				{
					var scale = (float)button.Tag;
					button.IsEnabled = allowed.Contains(scale);
					button.IsChecked = scale == settings.UIScale;
				}

				foreach (RadioButton button in ZoomRow.Children)
					button.IsChecked = (string)button.Tag == settings.ViewportDistance;

				VSyncBox.IsChecked = settings.VSync;
			}
			finally
			{
				updating = false;
			}

			ShowZoomHint();
			ShowSummary();
		}

		(List<ResolutionItem> Items, ResolutionItem Selected) ResolutionChoices(Display display)
		{
			var native = display.Native;
			var items = new List<ResolutionItem>();
			Resolution current;
			switch (settings.Mode)
			{
				case WindowMode.Fullscreen:
					items.AddRange(display.Modes
						.Where(r => r.Width >= MinUIWidth && r.Height >= MinUIHeight)
						.Select(r => new ResolutionItem(r, r == native ? $"{r}  (native)" : r.ToString())));
					current = settings.FullscreenWidth == 0 && settings.FullscreenHeight == 0
						? native : new Resolution(settings.FullscreenWidth, settings.FullscreenHeight);
					break;

				case WindowMode.Windowed:
				{
					// Leave room for the title bar and the taskbar.
					bool Fits(Resolution r) => r.Width <= native.Width && r.Height <= native.Height - 80;
					items.AddRange(WindowSizes.Where(Fits).Select(r => new ResolutionItem(r, r.ToString())));
					var windowed = new Resolution(settings.WindowedWidth, settings.WindowedHeight);
					if (Fits(windowed) && windowed.Width >= MinUIWidth && windowed.Height >= MinUIHeight && items.All(i => i.Size != windowed))
						items.Add(new ResolutionItem(windowed, windowed.ToString()));

					items.Sort((a, b) => (a.Size.Width * a.Size.Height).CompareTo(b.Size.Width * b.Size.Height));
					current = windowed;
					break;
				}

				default:
					items.Add(new ResolutionItem(native, $"{native}  (desktop)"));
					current = native;
					break;
			}

			if (items.Count == 0)
				items.Add(new ResolutionItem(native, $"{native}  (native)"));

			// The current choice, or the largest the monitor can take.
			var selected = items.FirstOrDefault(i => i.Size == current)
				?? (settings.Mode == WindowMode.Windowed ? items[^1] : items[0]);
			return (items, selected);
		}

		// Borderless always runs at the desktop size, which the engine reads as FullscreenSize 0,0:
		// any other size there leaves its drawing surface smaller than the window.
		void Apply(Resolution size)
		{
			var native = CurrentDisplay.Native;
			switch (settings.Mode)
			{
				case WindowMode.Fullscreen:
					(settings.FullscreenWidth, settings.FullscreenHeight) = size == native ? (0, 0) : (size.Width, size.Height);
					break;
				case WindowMode.Windowed:
					(settings.WindowedWidth, settings.WindowedHeight) = (size.Width, size.Height);
					break;
				default:
					(settings.FullscreenWidth, settings.FullscreenHeight) = (0, 0);
					break;
			}
		}

		static float SuggestedScale(Display display, Resolution size)
		{
			var maxScale = Math.Min(size.Width / (float)MinUIWidth, size.Height / (float)MinUIHeight);
			return UIScales.Where(s => s <= display.Scale + 0.01 && s <= maxScale + 0.001f).DefaultIfEmpty(1f).Max();
		}

		// The size the game will run at.
		Resolution CurrentSize() => settings.Mode switch
		{
			WindowMode.Fullscreen when settings.FullscreenWidth > 0 => new Resolution(settings.FullscreenWidth, settings.FullscreenHeight),
			WindowMode.Windowed => new Resolution(settings.WindowedWidth, settings.WindowedHeight),
			_ => CurrentDisplay.Native,
		};

		void ShowZoomHint()
		{
			ZoomHint.Text = Zooms.FirstOrDefault(z => z.Value == settings.ViewportDistance).Hint ?? "";
		}

		void ShowSummary()
		{
			var display = CurrentDisplay;
			var mode = settings.Mode switch
			{
				WindowMode.Fullscreen => "Fullscreen",
				WindowMode.Windowed => "Windowed",
				_ => "Borderless",
			};

			var size = CurrentSize();
			var monitor = displays.Count > 1 ? $"  ·  {display.Name}" : "";
			Summary.Text = $"{mode}  ·  {size}{monitor}  ·  Interface {settings.UIScale * 100:0}%";
		}

		// The line under the buttons: what stops the game starting, or what went wrong, with
		// the button that deals with it.
		void ShowProblem(string message = null, string action = null, Action onAction = null)
		{
			if (message == null)
			{
				message = install.Problem();
				if (install.HasEngine && !GameInstall.HasGameData)
					(action, onAction) = ("INSTALL FROM GAME…", Install);
				else if (message == null && GameInstall.CampaignMissions == 0)
					(message, action, onAction) = ("The campaign missions are not converted yet.", "INSTALL FROM GAME…", Install);
			}

			StatusText.Text = message ?? "";
			StatusRow.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
			ActionButton.Content = action;
			ActionButton.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
			this.onAction = onAction;
			PlayButton.IsEnabled = install.Problem() == null;
			DataStatus.Text = GameInstall.HasGameData
				? $"Installed  ·  {GameInstall.CampaignMissions} missions converted"
				: "Not installed";
		}

		Action onAction;

		void OnAction(object sender, RoutedEventArgs e) => onAction?.Invoke();

		bool SaveSettings()
		{
			try
			{
				settings.Save();
				return true;
			}
			catch (Exception e)
			{
				ShowProblem($"Could not save the settings: {e.Message}");
				return false;
			}
		}

		async void OnPlay(object sender, RoutedEventArgs e)
		{
			if (!PlayButton.IsEnabled || !SaveSettings())
				return;

			await RunGame();
		}

		// Starts the game and keeps the launcher, hidden, until it ends: closing with it, or coming
		// back with the logs if it failed. Also how the engine's own relaunches run (App.OnStartup),
		// so the engine sees a live process and does not take the restart for a failure.
		public async Task RunGame(IEnumerable<string> extraArgs = null)
		{
			PlayButton.IsEnabled = false;
			Process game;
			try
			{
				game = install.Start(extraArgs);
			}
			catch (Exception ex)
			{
				ShowProblem($"Could not start the game: {ex.Message}");
				Show();
				return;
			}

			Hide();
			await game.WaitForExitAsync();
			if (game.ExitCode == 0)
			{
				Application.Current.Shutdown();
				return;
			}

			settings = GraphicsSettings.Load();
			ShowSettings();
			ShowProblem($"Dark Reign closed unexpectedly (exit code {game.ExitCode}). The logs say why.", "OPEN LOGS", OpenLogs);
			Show();
			Activate();
		}

		void OnInstall(object sender, RoutedEventArgs e) => Install();

		// Asks for the player's copy of Dark Reign and runs import-campaign.ps1 on it.
		async void Install()
		{
			// Starting in a copy already on this PC, picking it is one click.
			var found = install.FindInstalledGame();
			var dialog = new OpenFolderDialog
			{
				Title = "Choose your Dark Reign folder (the one that holds the game's 'dark' folder)",
				InitialDirectory = found ?? "",
				FolderName = found ?? "",
			};

			if (dialog.ShowDialog(this) != true)
				return;

			var gameDir = GameInstall.FindGameDir(dialog.FolderName);
			if (gameDir == null)
			{
				ShowPage(Home);
				ShowProblem("That folder has no Dark Reign in it: choose the folder that holds the game's 'dark' folder.",
					"INSTALL FROM GAME…", Install);
				return;
			}

			InstallSource.Text = $"From {gameDir}";
			InstallLine.Text = "Starting…";
			ShowPage(InstallPage);
			var sweep = new DoubleAnimation(-ProgressSweep.Width, Math.Max(ProgressTrack.ActualWidth, 556), TimeSpan.FromSeconds(1.4))
			{
				RepeatBehavior = RepeatBehavior.Forever,
			};
			ProgressSweep.BeginAnimation(Canvas.LeftProperty, sweep);

			var lines = new List<string>();
			int code;
			installing = true;
			try
			{
				code = await install.Import(gameDir, line => Dispatcher.BeginInvoke(() =>
				{
					lines.Add(line);
					InstallLine.Text = line.Trim();
				}));
			}
			catch (Exception ex)
			{
				lines.Add(ex.Message);
				code = -1;
			}
			finally
			{
				installing = false;
			}

			ProgressSweep.BeginAnimation(Canvas.LeftProperty, null);
			ShowPage(Home);
			LoadArt();
			if (code == 0 && GameInstall.HasGameData)
				ShowProblem();
			else
				ShowProblem($"The install did not finish: {lines.LastOrDefault() ?? $"exit code {code}"}", "TRY AGAIN", Install);
		}

		bool installing;

		// Closing mid-install would leave the import running unseen, and the data half copied.
		protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
		{
			if (installing)
			{
				e.Cancel = true;
				InstallLine.Text = "Still installing: the launcher can close once this has finished.";
			}

			base.OnClosing(e);
		}

		void ShowPage(FrameworkElement page)
		{
			foreach (var p in new FrameworkElement[] { Home, SettingsPage, InstallPage })
				p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
		}

		void OnSettings(object sender, RoutedEventArgs e)
		{
			// Pick up anything the game's own menu changed since the launcher opened.
			settings = GraphicsSettings.Load();
			if (!settings.HasUIScale)
				settings.UIScale = SuggestedScale(CurrentDisplay, CurrentDisplay.Native);

			ShowSettings();
			ShowPage(SettingsPage);
		}

		void OnSettingsDone(object sender, RoutedEventArgs e)
		{
			if (!SaveSettings())
				return;

			ShowPage(Home);
			PlayButton.Focus();
		}

		void OnDefaults(object sender, RoutedEventArgs e)
		{
			var display = CurrentDisplay;
			settings = new GraphicsSettings { VideoDisplay = display.Index };
			settings.UIScale = SuggestedScale(display, display.Native);
			ShowSettings();
		}

		void OnModeChanged(object sender, RoutedEventArgs e)
		{
			if (updating)
				return;

			settings.Mode = sender == ModeFullscreen ? WindowMode.Fullscreen
				: sender == ModeWindowed ? WindowMode.Windowed : WindowMode.PseudoFullscreen;
			ShowSettings();
		}

		void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
		{
			if (updating || MonitorBox.SelectedItem is not Display display || display.Index == settings.VideoDisplay)
				return;

			settings.VideoDisplay = display.Index;

			// A resolution chosen for one monitor rarely suits another.
			settings.FullscreenWidth = settings.FullscreenHeight = 0;
			ShowSettings();
		}

		void OnResolutionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (updating || ResolutionBox.SelectedItem is not ResolutionItem item || item.Size == CurrentSize())
				return;

			Apply(item.Size);
			ShowSettings();
		}

		void OnVSyncChanged(object sender, RoutedEventArgs e)
		{
			if (!updating)
				settings.VSync = VSyncBox.IsChecked == true;
		}

		static void OpenLogs()
		{
			Directory.CreateDirectory(GameInstall.LogsDir);
			Process.Start(new ProcessStartInfo(GameInstall.LogsDir) { UseShellExecute = true });
		}

		void OnMinimise(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

		void OnExit(object sender, RoutedEventArgs e) => Close();

		void OnKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				if (SettingsPage.Visibility == Visibility.Visible)
					OnSettingsDone(sender, e);
				else if (Home.Visibility == Visibility.Visible)
					Close();

				e.Handled = true;
			}
			else if (e.Key == Key.Enter && Home.Visibility == Visibility.Visible && PlayButton.IsEnabled)
			{
				OnPlay(sender, e);
				e.Handled = true;
			}
		}
	}
}
