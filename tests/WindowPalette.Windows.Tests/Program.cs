using System.Diagnostics;
using WindowPalette.Core;
using WindowPalette.Windows;

if (args is ["--fixture"]) return DesktopFixture.Run();
if (args is ["--fixture-close"]) return DesktopFixture.Close() ? 0 : 1;
if (args is ["--desktop-info"]) { Console.WriteLine(DesktopFixture.ReadDiagnostics()); return 0; }
if (args is ["--control", var controlScope, var command])
{
    var reply = await WorkerConnection.SendAsync(command, 3500, controlScope);
    Console.WriteLine(reply is null ? "No response from test worker." : System.Text.Json.JsonSerializer.Serialize(reply, SerializationContext.Default.WorkerReply));
    return reply is null || reply.Error is not null ? 1 : 0;
}
if (args is ["--observe", var observeDirectory, var observeScope])
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
    return BackgroundProcess.Run(new SettingsStore(observeDirectory), observeScope, timeout.Token);
}

if (args is ["--worker", var directory, var instance])
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    return BackgroundProcess.Run(new SettingsStore(directory), instance, timeout.Token);
}

var root = Path.Combine(Path.GetTempPath(), "WindowPalette.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var results = new List<(string Name, bool Passed, string? Error)>();
var store = new SettingsStore(root);
var scope = ".test." + Guid.NewGuid().ToString("N");
var processes = new List<Process>();

async Task Check(string name, Func<Task> action)
{
    try { await action(); results.Add((name, true, null)); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { results.Add((name, false, exception.Message)); Console.WriteLine($"FAIL {name}: {exception.Message}"); }
}
void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
Process Launch()
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
    start.ArgumentList.Add("--worker"); start.ArgumentList.Add(root); start.ArgumentList.Add(scope);
    var process = Process.Start(start) ?? throw new InvalidOperationException("Test worker did not launch.");
    processes.Add(process);
    Console.WriteLine($"OWNED_WORKER_PID={process.Id}");
    return process;
}
async Task<WorkerReply> Ready()
{
    for (var attempt = 0; attempt < 30; attempt++)
    {
        var response = await WorkerConnection.SendAsync("status", 200, scope);
        if (response?.Running == true) return response;
        await Task.Delay(50);
    }
    throw new InvalidOperationException("The native event monitor did not become ready.");
}

try
{
    await Check("An unresponsive or other installed copy produces uncertain status", () =>
    {
        var uncertain = FeatureController.CreateStatus(new PaletteSettings(), null, true, "Disabled", "Check the other copy.");
        Assert(uncertain.WorkerRunning && uncertain.WorkerStatusUncertain && uncertain.DisplayStatus == "Automatic resizing status is uncertain", "A shared mutex was misreported as this package's confirmed worker.");
        var absent = FeatureController.CreateStatus(new PaletteSettings(), null, false, "Disabled", null);
        Assert(!absent.WorkerRunning && !absent.WorkerStatusUncertain && absent.DisplayStatus == "Automatic resizing is off", "An absent worker was not reported as off.");
        var confirmed = FeatureController.CreateStatus(new PaletteSettings { Enabled = true }, new WorkerReply(true, 1), true, "Enabled", null);
        Assert(confirmed.WorkerRunning && !confirmed.WorkerStatusUncertain && confirmed.DisplayStatus == "Automatic resizing is running", "A confirmed worker was not reported as running.");
        return Task.CompletedTask;
    });
    foreach (var mode in Enum.GetValues<ResizeMode>())
    {
        await Check($"{mode}: fitting release makes no window operation", () =>
        {
            var work = new PixelRect(-1000, 50, 1000, 1800);
            var request = new ResizeRequest(new PixelRect(0, 0, 1600, 600), new PixelRect(0, 0, 2000, 1000),
                work, new PixelPoint(-100, 200), 0.5, 20);
            var calls = 0;
            // Its position overflows, but its dimensions fit and must remain untouched.
            var result = WindowResizeOperation.Apply(new PixelRect(-200, 80, 900, 600), request, mode,
                _ => { calls++; return true; }, out _);
            Assert(result == ResizeOperationResult.AlreadyFits && calls == 0, "Fitting window caused a native operation.");
            return Task.CompletedTask;
        });
        await Check($"{mode}: oversized release invokes one calculated operation", () =>
        {
            var work = new PixelRect(-1000, 50, 1000, 1800);
            var request = new ResizeRequest(new PixelRect(0, 0, 1600, 600), new PixelRect(0, 0, 2000, 1000),
                work, new PixelPoint(-100, 200), 0.5, 20);
            var calls = 0;
            PixelRect applied = default;
            var result = WindowResizeOperation.Apply(new PixelRect(-200, 80, 1001, 600), request, mode,
                rect => { calls++; applied = rect; return true; }, out var desired);
            Assert(result == ResizeOperationResult.Applied && calls == 1 && applied == desired
                && applied == ResizeGeometry.Calculate(request, mode), "Oversized window did not receive exactly one expected operation.");
            return Task.CompletedTask;
        });
    }
    await Check("Missing settings use opt-in defaults", () =>
    {
        var settings = store.Load();
        Assert(!settings.Enabled && settings.StartWithWindows && settings.Mode == ResizeMode.MatchScreenProportions, "Defaults differ from the plan.");
        return Task.CompletedTask;
    });
    await Check("Atomic settings round trip retains both modes and preferences", () =>
    {
        foreach (var mode in Enum.GetValues<ResizeMode>())
        {
            var settings = new PaletteSettings { Enabled = true, Mode = mode, StartWithWindows = false };
            store.Save(settings);
            Assert(store.Load() == settings, "Saved settings changed.");
        }
        Assert(Directory.GetFiles(root, "*.tmp").Length == 0, "Temporary settings file was left behind.");
        return Task.CompletedTask;
    });
    await Check("Malformed settings are rejected without overwriting evidence", () =>
    {
        File.WriteAllText(store.FilePath, "{broken");
        try { store.Load(); throw new InvalidOperationException("Malformed settings were accepted."); }
        catch (System.Text.Json.JsonException) { }
        Assert(File.ReadAllText(store.FilePath) == "{broken", "Malformed input was overwritten.");
        return Task.CompletedTask;
    });
    await Check("Unknown settings versions are rejected", () =>
    {
        File.WriteAllText(store.FilePath, "{\"Version\":99,\"Settings\":{}}");
        try { store.Load(); throw new InvalidOperationException("Unknown version was accepted."); }
        catch (InvalidDataException) { }
        return Task.CompletedTask;
    });
    await Check("Disabled startup activation exits without a worker", async () =>
    {
        store.Save(new PaletteSettings());
        var process = Launch();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert(process.ExitCode == 0, "Disabled worker returned failure.");
        Assert(await WorkerConnection.SendAsync("status", 150, scope) is null, "Disabled activation started a listener.");
    });
    Process? worker = null;
    await Check("Enabled worker installs native hooks and answers status", async () =>
    {
        store.Save(new PaletteSettings { Enabled = true });
        worker = Launch();
        var reply = await Ready();
        Assert(reply.ProcessId == worker.Id && reply.Error is null, "Status does not identify the active worker.");
    });
    await Check("Duplicate activation preserves the original worker", async () =>
    {
        var duplicate = Launch();
        await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert(duplicate.ExitCode == 0, "Duplicate activation failed.");
        Assert((await Ready()).ProcessId == worker!.Id, "The original worker was replaced.");
    });
    await Check("Mode reload keeps the existing native worker", async () =>
    {
        store.Save(new PaletteSettings { Enabled = true, Mode = ResizeMode.KeepWindowShape, StartWithWindows = false });
        var reply = await WorkerConnection.SendAsync("reload", 1500, scope);
        Assert(reply?.Running == true && reply.ProcessId == worker!.Id && reply.Error is null, "Reload failed or replaced the worker.");
    });
    await Check("Unknown control request fails without stopping monitoring", async () =>
    {
        var reply = await WorkerConnection.SendAsync("unsupported", 1500, scope);
        Assert(reply?.Running == true && reply.Error is not null, "Unknown request was not rejected.");
        await Ready();
    });
    await Check("Unreadable settings preserve a live worker and allow shutdown", async () =>
    {
        File.WriteAllText(store.FilePath, "{broken");
        var reply = await WorkerConnection.SendAsync("reload", 1500, scope);
        Assert(reply?.Running == true && reply.Error is not null, "Settings failure hid or stopped the worker.");
        Assert(File.ReadAllText(store.FilePath) == "{broken", "Corrupt settings were overwritten.");
        Assert(WorkerConnection.IsInstancePresent(scope), "Live worker mutex was reported absent.");
        await WorkerConnection.SendAsync("shutdown", 1500, scope);
        await worker!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert(worker.ExitCode == 0 && !WorkerConnection.IsInstancePresent(scope), "Corrupt settings prevented explicit shutdown.");
        Assert(File.ReadAllText(store.FilePath) == "{broken", "Shutdown overwrote corrupt settings.");
        store.Save(new PaletteSettings { Enabled = true });
        worker = Launch();
        await Ready();
    });
    await Check("Busy control pipe is not evidence that monitoring stopped", async () =>
    {
        using (var held = new System.IO.Pipes.NamedPipeClientStream(".", ProcessIdentity.PipeName + scope,
            System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly))
        {
            await held.ConnectAsync(1000);
            Assert(await WorkerConnection.SendAsync("status", 150, scope) is null, "Busy pipe unexpectedly accepted another request.");
            Assert(WorkerConnection.IsInstancePresent(scope), "Busy worker was reported stopped.");
        }
        await Ready();
    });
    await Check("Disabling unregisters hooks and exits the process", async () =>
    {
        store.Save(new PaletteSettings());
        var reply = await WorkerConnection.SendAsync("reload", 1500, scope);
        Assert(reply?.Running == false, "Disable was not acknowledged.");
        await worker!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert(worker.ExitCode == 0, "Shutdown failed.");
        Assert(await WorkerConnection.SendAsync("status", 150, scope) is null, "Listener survived shutdown.");
    });
    await Check("Worker can restart and shut down without settings mutation", async () =>
    {
        store.Save(new PaletteSettings { Enabled = true });
        var restarted = Launch();
        await Ready();
        await WorkerConnection.SendAsync("shutdown", 1500, scope);
        await restarted.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert(restarted.ExitCode == 0 && store.Load().Enabled, "Explicit shutdown changed saved intent or failed.");
    });
}
finally
{
    await WorkerConnection.SendAsync("shutdown", 500, scope);
    foreach (var process in processes)
    {
        try
        {
            if (!process.HasExited)
            {
                if (!process.WaitForExit(3000))
                    Console.WriteLine($"CLEANUP_PENDING_PID={process.Id}; test worker has a 60-second cancellation deadline.");
            }
        }
        finally { process.Dispose(); }
    }
    // Only this fresh, absolute, task-owned test directory is removed.
    Directory.Delete(root, recursive: true);
}
Console.WriteLine($"Windows checks: {results.Count(result => result.Passed)} passed, {results.Count(result => !result.Passed)} failed.");
return results.All(result => result.Passed) ? 0 : 1;
