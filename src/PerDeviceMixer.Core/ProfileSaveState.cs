namespace PerDeviceMixer.Core;

public enum ProfileSaveState
{
    Pending,
    Saved,
    Failed
}

public sealed class ProfileSaveStateChangedEventArgs(
    ProfileSaveState state,
    Exception? exception = null) : EventArgs
{
    public ProfileSaveState State { get; } = state;
    public Exception? Exception { get; } = exception;
}
