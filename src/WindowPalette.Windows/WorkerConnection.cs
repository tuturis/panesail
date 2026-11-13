using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace WindowPalette.Windows;

internal static class WorkerConnection
{
    internal static bool IsInstancePresent(string? instanceName = null)
    {
        try
        {
            if (!Mutex.TryOpenExisting(ProcessIdentity.WorkerMutex + instanceName, out var mutex)) return false;
            using (mutex)
            {
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return true;
                mutex.ReleaseMutex();
                return false;
            }
        }
        catch (UnauthorizedAccessException) { return true; }
    }

    internal static async Task<WorkerReply?> SendAsync(string command, int timeoutMilliseconds = 1500, string? instanceName = null)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        using var pipe = new NamedPipeClientStream(".", ProcessIdentity.PipeName + instanceName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new WorkerRequest(command), SerializationContext.Default.WorkerRequest).AsMemory(), timeout.Token).ConfigureAwait(false);
            var line = await ReadBoundedLineAsync(reader, timeout.Token).ConfigureAwait(false);
            return JsonSerializer.Deserialize(line, SerializationContext.Default.WorkerReply);
        }
        catch (OperationCanceledException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static async Task<string> ReadBoundedLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var character = new char[1];
        while (result.Length < 8192)
        {
            if (await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false) == 0) throw new IOException("Control connection closed.");
            if (character[0] == '\n') return result.ToString();
            if (character[0] != '\r') result.Append(character[0]);
        }
        throw new InvalidDataException("Control message is too large.");
    }
}
