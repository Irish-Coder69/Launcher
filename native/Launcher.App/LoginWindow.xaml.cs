using System.Windows;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App;

public partial class LoginWindow : Window
{
    private readonly LauncherUserProfileService _userProfileService;

    public LoginWindow(LauncherUserProfileService userProfileService)
    {
        InitializeComponent();
        _userProfileService = userProfileService;
        LoadUsers();
    }

    public LauncherUserProfile? AuthenticatedUser { get; private set; }

    private void LoadUsers(string? preferredUserName = null)
    {
        var users = _userProfileService.GetUsers();
        UserComboBox.ItemsSource = users;
        if (users.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(preferredUserName))
            {
                var match = users.FirstOrDefault(user => string.Equals(user.UserName, preferredUserName, StringComparison.OrdinalIgnoreCase));
                UserComboBox.SelectedItem = match;
            }

            if (UserComboBox.SelectedItem is null)
            {
                UserComboBox.SelectedIndex = 0;
            }

            LoginStatusTextBlock.Text = "Select a user and sign in.";
        }
        else
        {
            LoginStatusTextBlock.Text = "No users exist yet. Create the first user to continue.";
        }
    }

    private void LoginButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedUserName = UserComboBox.SelectedValue?.ToString();
        if (!_userProfileService.TryAuthenticate(selectedUserName ?? string.Empty, PasswordBox.Password, out var user, out var errorMessage) || user is null)
        {
            MessageBox.Show(this, errorMessage, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _userProfileService.RecordLogin(user.UserName, IssueNoteTextBox.Text);
        AuthenticatedUser = user;
        DialogResult = true;
        Close();
    }

    private void CreateUserButton_OnClick(object sender, RoutedEventArgs e)
    {
        var createWindow = new CreateUserWindow(_userProfileService)
        {
            Owner = this
        };

        if (createWindow.ShowDialog() == true)
        {
            LoadUsers();
        }
    }

    private void EditUserButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (UserComboBox.SelectedItem is not LauncherUserProfile selectedUser)
        {
            MessageBox.Show(this, "Select a user to edit first.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editWindow = new EditUserWindow(_userProfileService, selectedUser)
        {
            Owner = this
        };

        if (editWindow.ShowDialog() == true)
        {
            LoadUsers(editWindow.UpdatedUserName);
            PasswordBox.Clear();
            LoginStatusTextBlock.Text = "User updated. Sign in with the latest credentials.";
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
