namespace LfWindows.Messages;

public enum ScrollDirection
{
    Up,
    Down,
    Left,
    Right,
    PageUp,
    PageDown,
    Home,
    End
}

public class ScrollPreviewMessage
{
    public ScrollDirection Direction { get; }

    public ScrollPreviewMessage(ScrollDirection direction)
    {
        Direction = direction;
    }
}
