using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace WindowPalette.Windows;

public static class BackgroundProcess
{
    public static int Run(SettingsStore? settingsStore = null, string? instanceName = null, CancellationToken cancellationToken = default)
    {
        using var singleton = new Mutex(false, ProcessIdentity.WorkerMutex + instanceName);
        bool ownsInstance;
        try { ownsInstance = singleton.WaitOne(0); }
        catch (AbandonedMutexException) { ownsInstance = true; }
        if (!ownsInstance) return 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var store = settingsStore ?? new SettingsStore();
            var settings = store.Load();
            if (!settings.Enabled) return 0;
            string? lastActivity = "Waiting for a window drag.";
            using var monitor = new WindowEventMonitor(settings, message => Volatile.Write(ref lastActivity, message));
            // The control server runs independently of the native message loop.
            var server = ServeAsync(store, monitor, () => Volatile.Read(ref lastActivity), cancellation, instanceName);
            _ = server.ContinueWith(_ => cancellation.Cancel(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            try { monitor.Run(cancellation.Token); }
            finally
            {
                cancellation.Cancel();
                server.GetAwaiter().GetResult();
            }
            return 0;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("PaneSail background stopped: {0}", exception.Message);
            return 1;
        }
        finally { singleton.ReleaseMutex(); }
    }

    private static async Task ServeAsync(SettingsStore store, WindowEventMonitor monitor, Func<string?> activity, CancellationTokenSource lifetime, string? instanceName)
    {
        while (!lifetime.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(ProcessIdentity.PipeName + instanceName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var stop = false;
            try
            {
                await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var line = await WorkerConnection.ReadBoundedLineAsync(reader, requestTimeout.Token).ConfigureAwait(false);
                WorkerReply reply;
                try
                {
                    var request = JsonSerializer.Deserialize(line, SerializationContext.Default.WorkerRequest);
                    switch (request?.Command)
                    {
                        case "status": break;
                        case "reload":
                            var settings = store.Load();
                            monitor.UpdateSettings(settings);
                            stop = !settings.Enabled;
                            break;
                        case "shutdown":
                            monitor.UpdateSettings(new WindowPalette.Core.PaletteSettings { Enabled = false });
                            stop = true;
                            break;
                        default: throw new InvalidDataException("Unknown control request.");
                    }
                    reply = new WorkerReply(!stop && monitor.IsRunning, Environment.ProcessId, activity());
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Trace.TraceError("Control request failed: {0}", exception);
                    reply = new WorkerReply(monitor.IsRunning, Environment.ProcessId, activity(), "The background component could not apply the saved settings.");
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(reply, SerializationContext.Default.WorkerReply).AsMemory(), requestTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally { if (stop) lifetime.Cancel(); }
        }
    }
}
