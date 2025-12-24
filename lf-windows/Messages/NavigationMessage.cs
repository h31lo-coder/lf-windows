namespace LfWindows.Messages;

public enum NavigationType
{
    ScreenTop,
    ScreenMiddle,
    ScreenBottom,
    PageUp,
    PageDown,
    HalfPageUp,
    HalfPageDown
}

public class NavigationMessage
{
    public NavigationType Type { get; }

    public NavigationMessage(NavigationType type)
    {
        Type = type;
    }
}
