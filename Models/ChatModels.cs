using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MessengerClient.Models;

public abstract class BindableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public class UserProfile : BindableObject
{
    private int _userId;
    private string _displayName = "Олексій Користувач";
    private string _status = "У мережі";

    public int UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}

public class Chat : BindableObject
{
    private int _userId;
    private string _title = string.Empty;
    private string _preview = string.Empty;
    private string _time = string.Empty;
    private bool _isOnline;

    public int UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Preview
    {
        get => _preview;
        set => SetProperty(ref _preview, value);
    }

    public string Time
    {
        get => _time;
        set => SetProperty(ref _time, value);
    }

    public bool IsOnline
    {
        get => _isOnline;
        set => SetProperty(ref _isOnline, value);
    }

    public ObservableCollection<Message> Messages { get; } = new();
}

public class Message : BindableObject
{
    private long _id;
    private int _senderId;
    private int _receiverId;
    private string _text = string.Empty;
    private string _author = string.Empty;
    private string _time = string.Empty;
    private string _dateTimeDisplay = string.Empty;
    private bool _isOwn;

    public DateTime? LocalDate { get; set; }
    public string DateLabel { get; set; } = string.Empty;
    public bool ShowDateSeparator { get; set; }

    public long Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public int SenderId
    {
        get => _senderId;
        set => SetProperty(ref _senderId, value);
    }

    public int ReceiverId
    {
        get => _receiverId;
        set => SetProperty(ref _receiverId, value);
    }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public string Author
    {
        get => _author;
        set => SetProperty(ref _author, value);
    }

    public string Time
    {
        get => _time;
        set => SetProperty(ref _time, value);
    }

    public string DateTimeDisplay
    {
        get => _dateTimeDisplay;
        set => SetProperty(ref _dateTimeDisplay, value);
    }

    public bool IsOwn
    {
        get => _isOwn;
        set => SetProperty(ref _isOwn, value);
    }
}
