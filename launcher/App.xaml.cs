using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace DarkReign.Launcher
{
	public partial class App : Application
	{
		// Held for the life of the launcher a player opened, so that opening it again brings
		// back what is already running instead of a second launcher.
		static Mutex instance;

		protected override async void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			var window = new MainWindow();
			MainWindow = window;

			// Started with arguments, the launcher is the engine relaunching itself (after changing
			// a setting that needs a restart, or to open a replay): start the game with them, and
			// show the window only if it fails.
			if (e.Args.Length > 0)
			{
				try
				{
					var settings = GraphicsSettings.Load();
					if (settings.FixBorderlessSize())
						settings.Save();
				}
				catch (Exception)
				{
					// The game still starts; borderless may just not fill the screen.
				}

				if (GameInstall.Locate().Problem() is null)
				{
					await window.RunGame(e.Args);
					return;
				}
			}
			else
			{
				instance = new Mutex(true, @"Local\DarkReign.Launcher", out var first);
				if (!first)
				{
					BringForward();
					Shutdown();
					return;
				}
			}

			window.Show();
		}

		// The launcher's window if it is showing, otherwise the game's: the launcher hides while the game runs.
		static void BringForward()
		{
			var current = Environment.ProcessId;
			var window = Process.GetProcessesByName("DarkReign").Where(p => p.Id != current)
				.Concat(Process.GetProcessesByName("DarkReignGame"))
				.Select(p => p.MainWindowHandle)
				.FirstOrDefault(h => h != IntPtr.Zero);

			if (window == IntPtr.Zero)
				return;

			if (IsIconic(window))
				ShowWindow(window, SwRestore);

			SetForegroundWindow(window);
		}

		const int SwRestore = 9;

		[DllImport("user32.dll")]
		static extern bool SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		static extern bool IsIconic(IntPtr hWnd);
	}
}
