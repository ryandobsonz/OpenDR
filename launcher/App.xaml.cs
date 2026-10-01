using System;
using System.Windows;

namespace DarkReign.Launcher
{
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			// Started with arguments, the launcher is the engine relaunching itself (switching mods,
			// opening a replay): start the game with them and show nothing.
			if (e.Args.Length > 0)
			{
				var install = GameInstall.Locate();
				if (install.Problem() is string problem)
					MessageBox.Show(problem, "Dark Reign", MessageBoxButton.OK, MessageBoxImage.Warning);
				else
					install.Start(e.Args);

				Shutdown();
				return;
			}

			MainWindow = new MainWindow();
			MainWindow.Show();
		}
	}
}
