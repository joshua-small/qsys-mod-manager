using System.Security.Principal;
using System.Windows.Threading;

namespace QdsModManager.App;

internal sealed class UserInstance : IDisposable
{
    private readonly Dictionary<UpdateCommand, EventWaitHandle> commands = new();
    private readonly Mutex mutex;
    private readonly List<RegisteredWaitHandle> waits = new();
    internal bool IsFirst { get; }
    internal UserInstance(string? name = null)
    {
        name ??= @"Local\QsysModManager." + WindowsIdentity.GetCurrent().User!.Value;
        // Create the event first so a simultaneous second launch cannot lose its activation.
        foreach (var command in Enum.GetValues<UpdateCommand>())
            commands.Add(command, new EventWaitHandle(false, EventResetMode.AutoReset, name + (command == UpdateCommand.Open ? ".Activate" : "." + command)));
        mutex = new Mutex(false, name + ".UserInterface", out bool first); IsFirst = first;
    }
    internal void Activate(UpdateCommand command = UpdateCommand.Open) => commands[command].Set();
    internal void Listen(Dispatcher dispatcher, Action show) => Listen(dispatcher, _ => show());
    internal void Listen(Dispatcher dispatcher, Action<UpdateCommand> run)
    {
        foreach (var (command, signal) in commands) waits.Add(ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
        {
            if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(() => run(command));
        }, null, Timeout.Infinite, false));
    }
    public void Dispose() { foreach (var wait in waits) wait.Unregister(null); mutex.Dispose(); foreach (var signal in commands.Values) signal.Dispose(); }
}
