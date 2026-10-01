using System;
using System.Windows;

namespace DarkReign.Launcher
{
	public partial class App : Application
	{
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

			window.Show();
		}
	}
}
