using System.Windows;
using MessengerClient.Models;

namespace MessengerClient;

public partial class SettingsWindow : Window
{
    private readonly UserProfile _profile;

    public SettingsWindow(UserProfile profile)
    {
        InitializeComponent();
        _profile = profile;
        DataContext = _profile;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_profile.DisplayName))
            {
                throw new InvalidOperationException("Ім’я користувача не може бути порожнім.");
            }

            InfoTextBlock.Text = "Налаштування збережено.";
            DialogResult = true;
        }
        catch (Exception exception)
        {
            InfoTextBlock.Text = exception.Message;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
