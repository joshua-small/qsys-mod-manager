using System.Security.Principal;
using System.Windows.Threading;

namespace QdsModManager.App;

internal sealed class UserInstance : IDisposable
{
    private readonly EventWaitHandle activate;
    private readonly Mutex mutex;
    private RegisteredWaitHandle? wait;
    internal bool IsFirst { get; }
    internal UserInstance(string? name = null)
    {
        name ??= @"Local\QsysModManager." + WindowsIdentity.GetCurrent().User!.Value;
        // Create the event first so a simultaneous second launch cannot lose its activation.
        activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        mutex = new Mutex(false, name + ".UserInterface", out bool first); IsFirst = first;
    }
    internal void Activate() => activate.Set();
    internal void Listen(Dispatcher dispatcher, Action show)
    {
        wait = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) =>
        {
            if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(show);
        }, null, Timeout.Infinite, false);
    }
    public void Dispose() { wait?.Unregister(null); mutex.Dispose(); activate.Dispose(); }
}
