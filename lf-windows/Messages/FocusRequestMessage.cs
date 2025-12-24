namespace LfWindows.Messages;

public enum FocusTarget
{
    MainList,
    CommandLine,
    WorkspacePanel,
    WorkspaceItems
}

public class FocusRequestMessage
{
    public FocusTarget Target { get; }
    public FocusRequestMessage(FocusTarget target)
    {
        Target = target;
    }
}
