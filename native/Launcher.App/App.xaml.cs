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
		mainWindow.Show();
	}
}

