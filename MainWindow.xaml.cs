using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MessengerClient.Models;

namespace MessengerClient;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<Chat> _allChats = new();

    public ObservableCollection<Chat> Chats { get; } = new();
    public UserProfile CurrentUser { get; } = new();
    private Chat? _selectedChat;

    public Chat? SelectedChat
    {
        get => _selectedChat;
        set
        {
            if (ReferenceEquals(_selectedChat, value))
            {
                return;
            }

            _selectedChat = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public MainWindow(string userName = "Олексій Користувач")
    {
        InitializeComponent();
        CurrentUser.DisplayName = userName;
        LoadDemoChats();
        DataContext = this;
        SelectedChat = null;
        ChatsListBox.SelectedItem = null;
        UpdateChatState();
    }

    private void LoadDemoChats()
    {
        var anna = new Chat { Title = "Анна Петренко", Preview = "До зустрічі!", Time = "12:40", IsOnline = true };
        anna.Messages.Add(new Message { Author = "Анна Петренко", Text = "Привіт! Не забудь сьогодні купити хліб.", Time = "12:37" });
        anna.Messages.Add(new Message { Author = CurrentUser.DisplayName, Text = "Привіт! Добре, заїду в магазин після роботи.", Time = "12:39", IsOwn = true });
        anna.Messages.Add(new Message { Author = "Анна Петренко", Text = "Дякую, до зустрічі ввечері!", Time = "12:40" });

        var max = new Chat { Title = "Максим Коваль", Preview = "Буду через десять хвилин", Time = "Вчора", IsOnline = false };
        max.Messages.Add(new Message { Author = "Максим Коваль", Text = "Буду через десять хвилин, вже виходжу.", Time = "Вчора" });

        var olya = new Chat { Title = "Оля", Preview = "", Time = "", IsOnline = true };

        _allChats.Add(anna);
        _allChats.Add(max);
        _allChats.Add(olya);
        foreach (var chat in _allChats)
        {
            Chats.Add(chat);
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = MessageTextBox.Text.Trim();
            if (SelectedChat is null)
            {
                throw new InvalidOperationException("Спочатку виберіть чат.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Введіть текст повідомлення.");
            }

            SelectedChat.Messages.Add(new Message
            {
                Author = CurrentUser.DisplayName,
                Text = text,
                Time = DateTime.Now.ToString("HH:mm"),
                IsOwn = true
            });
            SelectedChat.Preview = text;
            SelectedChat.Time = "щойно";
            MessageTextBox.Clear();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Помилка відправлення", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SendButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchTextBox.Text.Trim();
        Chats.Clear();
        foreach (var chat in _allChats.Where(chat => string.IsNullOrWhiteSpace(query) || chat.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            Chats.Add(chat);
        }
    }

    private void ChatsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChatsListBox.SelectedItem is Chat chat)
        {
            SelectedChat = chat;
        }

        UpdateChatState();
    }

    private void UpdateChatState()
    {
        EmptyChatState.Visibility = SelectedChat is null ? Visibility.Visible : Visibility.Collapsed;
        ChatContent.Visibility = SelectedChat is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("У наступній лабораторній роботі тут буде реалізовано пошук користувача та створення чату.", "Новий чат", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(CurrentUser) { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = new LoginWindow();
        Application.Current.MainWindow = loginWindow;
        loginWindow.Show();
        Close();
    }
}