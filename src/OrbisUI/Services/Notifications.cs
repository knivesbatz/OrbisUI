namespace OrbisUI.Services;

public sealed record Notice(string Title, string Body, string Glyph, DateTime When);

/// <summary>Launcher notifications: toasts plus the history shown in the Notifications screen.</summary>
public static class Notifications
{
    static readonly List<Notice> _items = new();
    public static IReadOnlyList<Notice> Items => _items;
    public static int Unread { get; private set; }
    public static event Action<Notice>? Posted;

    public static void Post(string title, string body = "", string glyph = "")
    {
        var n = new Notice(title, body, glyph, DateTime.Now);
        _items.Insert(0, n);
        if (_items.Count > 100) _items.RemoveAt(_items.Count - 1);
        Unread++;
        Posted?.Invoke(n);
    }

    public static void MarkRead() => Unread = 0;
}
