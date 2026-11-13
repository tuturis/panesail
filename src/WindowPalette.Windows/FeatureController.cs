using System.Diagnostics;
using WindowPalette.Core;
using global::Windows.ApplicationModel;

namespace WindowPalette.Windows;

public sealed record FeatureStatus(PaletteSettings Settings, bool WorkerRunning, string StartupState, string? Error = null, string? LastActivity = null, bool WorkerStatusUncertain = false)
{
    public string DisplayStatus => WorkerStatusUncertain ? "Automatic resizing status is uncertain" :
        WorkerRunning ? "Automatic resizing is running" :
        Settings.Enabled ? "Automatic resizing is not running" : "Automatic resizing is off";
}

public sealed class FeatureController
{
#if PANESAIL_STORE
    public const string StartupTaskId = "PaneSail.Background";
#else
    public const string StartupTaskId = "WindowPalette.Background";
#endif
    private static readonly SemaphoreSlim Changes = new(1, 1);
    private readonly SettingsStore _store = new();
    private readonly string _executablePath;

    public FeatureController(string? executablePath = null) => _executablePath = executablePath ?? Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate PaneSail.");

    public async Task<FeatureStatus> GetStatusAsync()
    {
        var worker = await WorkerConnection.SendAsync("status", 350).ConfigureAwait(false);
        var instancePresent = WorkerConnection.IsInstancePresent();
        string? error = worker is null && instancePresent ? "This copy is not responding, or another installed copy is running. Stop the other copy before starting this one." : worker?.Error;
        PaletteSettings settings;
        try { settings = _store.Load(); }
        catch (Exception exception)
        {
            Trace.TraceError("Settings read failed: {0}", exception);
            settings = new PaletteSettings();
            error = Join(error, "Could not read saved settings. The existing file has been preserved; you can still stop automatic resizing.");
        }
        var (startup, startupError) = await GetStartupStateAsync().ConfigureAwait(false);
        return CreateStatus(settings, worker, instancePresent, startup, Join(error, startupError));
    }

    // A shared arbitration mutex alone cannot tell whether this package's worker
    // is active: another installed package may own it and use a different pipe.
    internal static FeatureStatus CreateStatus(PaletteSettings settings, WorkerReply? worker, bool instancePresent, string startup, string? error) =>
        new(settings, worker?.Running ?? instancePresent, startup, error, worker?.LastActivity, worker is null && instancePresent);

    public async Task<FeatureStatus> ApplySettingsAsync(PaletteSettings settings)
    {
        await Changes.WaitAsync().ConfigureAwait(false);
        string? error = null;
        try
        {
            // Do not replace an unreadable settings file with defaults.
            try
            {
                _store.Load();
                _store.Save(settings);
            }
            catch (Exception exception) when (!settings.Enabled)
            {
                Trace.TraceError("Settings could not be saved while disabling: {0}", exception);
                error = "Saved settings could not be changed. The existing file is preserved; stopping this session anyway.";
            }
            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId).AsTask().ConfigureAwait(false);
                if (settings.Enabled && settings.StartWithWindows)
                {
                    var state = task.State;
                    if (state == StartupTaskState.Disabled) state = await task.RequestEnableAsync().AsTask().ConfigureAwait(false);
                    if (state != StartupTaskState.Enabled && state != StartupTaskState.EnabledByPolicy)
                        error = Join(error, "Windows has disabled startup. Enable PaneSail in Windows Settings → Apps → Startup to resume at sign-in.");
                }
                else task.Disable();
            }
            catch (Exception exception)
            {
                Trace.TraceError("Startup update failed: {0}", exception);
                error = Join(error, "Could not update Windows startup. Check its state in Windows Settings.");
            }

            if (settings.Enabled)
            {
                var worker = await WorkerConnection.SendAsync("reload", 350).ConfigureAwait(false);
                if (worker is null && !WorkerConnection.IsInstancePresent())
                {
                    using var process = Process.Start(new ProcessStartInfo(_executablePath, "--background") { UseShellExecute = false, CreateNoWindow = true });
                    for (var attempt = 0; attempt < 15 && worker?.Running != true; attempt++)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                        worker = await WorkerConnection.SendAsync("status", 200).ConfigureAwait(false);
                    }
                }
                if (worker?.Running != true) error = Join(error, "Automatic resizing could not start. Try enabling it again.");
                else if (worker.Error is not null) error = Join(error, worker.Error);
            }
            else
            {
                await WorkerConnection.SendAsync("shutdown", 3500).ConfigureAwait(false);
                var stopped = false;
                for (var attempt = 0; attempt < 25; attempt++)
                {
                    if (!WorkerConnection.IsInstancePresent()) { stopped = true; break; }
                    await Task.Delay(100).ConfigureAwait(false);
                }
                if (!stopped) error = Join(error, "The background component has not stopped yet. Automatic resizing may still be active.");
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError("Settings apply failed: {0}", exception);
            error = Join(error, "Could not apply settings. Check that the app can save its settings and try again.");
        }
        finally { Changes.Release(); }
        var status = await GetStatusAsync().ConfigureAwait(false);
        return status with { Error = Join(error, status.Error) };
    }

    private static async Task<(string State, string? Error)> GetStartupStateAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId).AsTask().ConfigureAwait(false);
            return (task.State.ToString(), null);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Startup query failed: {0}", exception);
            return ("Unavailable", "Windows startup status is unavailable. Open this extension from its installed package.");
        }
    }

    private static string? Join(string? first, string? second) => string.IsNullOrWhiteSpace(first) ? second : string.IsNullOrWhiteSpace(second) || first == second ? first : $"{first} {second}";
}
