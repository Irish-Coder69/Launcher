using System.Configuration;
using System.Data;
using System.Windows;
using Launcher.Core.Services;

namespace Launcher.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		// Keep the app alive while the login dialog is shown. Otherwise closing the
		// only open window can trigger app shutdown before MainWindow is created.
		ShutdownMode = ShutdownMode.OnExplicitShutdown;

		var userProfileService = new LauncherUserProfileService();
		var loginWindow = new LoginWindow(userProfileService);
		var loginResult = loginWindow.ShowDialog();
		if (loginResult != true)
		{
			Shutdown();
			return;
		}

		var mainWindow = new MainWindow(loginWindow.AuthenticatedUser);
		MainWindow = mainWindow;
		ShutdownMode = ShutdownMode.OnMainWindowClose;
		mainWindow.Show();
	}
}

