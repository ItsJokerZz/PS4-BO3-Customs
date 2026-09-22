using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFPorter.Desktop.Models;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Raise(name!);
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class RunStates
{
    public const string Ready = "ready", Queued = "queued", Running = "running", Done = "done", Failed = "failed", Cancelled = "cancelled";
}
