using System.Windows;
using System.Windows.Input;
using MessengerClient.Models;

namespace MessengerClient;

public partial class NewChatWindow : Window
{
    public Chat? SelectedUser => UsersListBox.SelectedItem as Chat;

    public NewChatWindow(IEnumerable<Chat> users)
    {
        InitializeComponent();
        UsersListBox.ItemsSource = users;
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedUser is null)
        {
            MessageBox.Show("Виберіть користувача зі списку.", "Новий чат", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void UsersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedUser is not null)
        {
            DialogResult = true;
        }
    }
}
