using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowPalette.Core;

namespace WindowPalette.Windows;

public sealed class SettingsStore
{
    private readonly string _directory;

    public SettingsStore(string? directory = null) => _directory = directory ?? DefaultDirectory();

    public string FilePath => Path.Combine(_directory, "settings.json");

    public PaletteSettings Load()
    {
        using var mutex = new Mutex(false, ProcessIdentity.SettingsMutex);
        Enter(mutex);
        try
        {
            if (!File.Exists(FilePath)) return new PaletteSettings();
            var stored = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SerializationContext.Default.StoredSettings)
                ?? throw new InvalidDataException("Settings are empty.");
            if (stored.Version != 1 || stored.Settings is null || !Enum.IsDefined(stored.Settings.Mode))
                throw new InvalidDataException("Settings have an unsupported format.");
            return stored.Settings;
        }
        finally { mutex.ReleaseMutex(); }
    }

    public void Save(PaletteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Mode)) throw new ArgumentException("Unknown resize mode.", nameof(settings));
        using var mutex = new Mutex(false, ProcessIdentity.SettingsMutex);
        Enter(mutex);
        try
        {
            Directory.CreateDirectory(_directory);
            var temporary = Path.Combine(_directory, $"settings.{Guid.NewGuid():N}.tmp");
            try
            {
                var json = JsonSerializer.Serialize(new StoredSettings(1, settings), SerializationContext.Default.StoredSettings);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, FilePath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static void Enter(Mutex mutex)
    {
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(3))) throw new IOException("Settings are busy. Try again.");
        }
        catch (AbandonedMutexException) { /* This caller now owns the mutex. */ }
    }

    private static string DefaultDirectory()
    {
        try { return global::Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
        catch (InvalidOperationException) { return FallbackDirectory; }
        catch (System.Runtime.InteropServices.COMException) { return FallbackDirectory; }
    }

    // Package-local storage is distinct across installations. Keep that separation when
    // package storage is unavailable, while preserving the existing development path.
    internal static string FallbackDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
#if PANESAIL_STORE
        "PaneSail");
#else
        "WindowPalette");
#endif
}

internal static class ProcessIdentity
{
    private static readonly string User = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Unable to identify the current user.");
    private static readonly string Session = System.Diagnostics.Process.GetCurrentProcess().SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
#if PANESAIL_STORE
    public static string PipeName => $"PaneSail.{User}.{Session}";
#else
    public static string PipeName => $"WindowPalette.{User}.{Session}";
#endif
    public static string WorkerMutex => $"Local\\WindowPalette.Background.{User}.{Session}";
    public static string SettingsMutex => $"Local\\WindowPalette.Settings.{User}";
}

internal sealed record StoredSettings(int Version, PaletteSettings Settings);
internal sealed record WorkerRequest(string Command);
internal sealed record WorkerReply(bool Running, int ProcessId, string? LastActivity = null, string? Error = null);

[JsonSerializable(typeof(StoredSettings))]
[JsonSerializable(typeof(WorkerRequest))]
[JsonSerializable(typeof(WorkerReply))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class SerializationContext : JsonSerializerContext;
