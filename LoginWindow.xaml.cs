using System.Windows;

namespace MessengerClient;

public partial class LoginWindow : Window
{
    private bool _isRegistrationMode;

    public LoginWindow()
    {
        InitializeComponent();
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorTextBlock.Text = string.Empty;
            var username = UsernameTextBox.Text.Trim();
            var password = PasswordInput.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Заповніть логін і пароль.");
            }

            if (password.Length < 4)
            {
                throw new InvalidOperationException("Пароль повинен містити щонайменше 4 символи.");
            }

            var message = _isRegistrationMode
                ? $"Профіль «{username}» успішно створено."
                : $"Вітаємо, {username}!";

            MessageBox.Show(message, _isRegistrationMode ? "Реєстрація" : "Авторизація",
                MessageBoxButton.OK, MessageBoxImage.Information);

            var mainWindow = new MainWindow(username);
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        }
        catch (Exception exception)
        {
            ErrorTextBlock.Text = exception.Message;
        }
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isRegistrationMode = !_isRegistrationMode;
        ActionButton.Content = _isRegistrationMode ? "Зареєструватися" : "Увійти";
        ModeButton.Content = _isRegistrationMode
            ? "Вже є обліковий запис? Увійти"
            : "Немає облікового запису? Зареєструватися";
        ErrorTextBlock.Text = string.Empty;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        UsernameTextBox.Clear();
        PasswordInput.Clear();
        ErrorTextBlock.Text = string.Empty;
        UsernameTextBox.Focus();
    }
}
