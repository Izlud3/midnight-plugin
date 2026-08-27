using System.Text.Json;
using MidnightPlugin.Core;

namespace MidnightPlugin;

public sealed class PersistentDiagnosticLog : IDisposable
{
    public const int DefaultCapacity = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly object syncRoot = new();
    private readonly DiagnosticLogBuffer buffer;
    private readonly string filePath;
    private bool dirty;
    private bool disposed;
    private DateTimeOffset lastFlush = DateTimeOffset.MinValue;

    public PersistentDiagnosticLog(string configDirectory, int capacity = DefaultCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);

        buffer = new DiagnosticLogBuffer(capacity);
        filePath = Path.Combine(configDirectory, "midnighttimeline-diagnostics.json");
        Load();
    }

    public string FilePath => filePath;

    public IReadOnlyList<DiagnosticLogEntry> Snapshot() => buffer.Snapshot();

    public void Add(string stage, uint? actionId, string message)
    {
        Add(new DiagnosticLogEntry(DateTimeOffset.Now, stage, actionId, message));
    }

    public void AddOnce(string key, string stage, uint? actionId, string message)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (buffer.AddOnce(key, new DiagnosticLogEntry(DateTimeOffset.Now, stage, actionId, message)))
            {
                dirty = true;
            }
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            buffer.Clear();
            dirty = true;
        }

        Flush();
    }

    public void FlushIfDue(TimeSpan interval)
    {
        if (interval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Flush interval cannot be negative.");
        }

        lock (syncRoot)
        {
            if (!dirty || DateTimeOffset.UtcNow - lastFlush < interval)
            {
                return;
            }
        }

        Flush();
    }

    public void Flush()
    {
        lock (syncRoot)
        {
            ThrowIfDisposed(allowDispose: true);
            if (!dirty)
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }

                Directory.CreateDirectory(directory);
                var temporaryPath = $"{filePath}.tmp";
                var contents = JsonSerializer.Serialize(buffer.Snapshot(), SerializerOptions);
                File.WriteAllText(temporaryPath, contents);
                File.Move(temporaryPath, filePath, overwrite: true);
                dirty = false;
                lastFlush = DateTimeOffset.UtcNow;
            }
            catch (Exception exception)
            {
                // Diagnostics must never interfere with the plugin's capture or UI paths.
                System.Diagnostics.Debug.WriteLine($"Unable to persist Midnight Timeline diagnostics: {exception}");
            }
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }
        }

        Flush();

        lock (syncRoot)
        {
            disposed = true;
        }
    }

    private void Add(DiagnosticLogEntry entry)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            buffer.Add(entry);
            dirty = true;
        }
    }

    private void Load()
    {
        try
        {
            var temporaryPath = $"{filePath}.tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            if (!File.Exists(filePath))
            {
                return;
            }

            var contents = File.ReadAllText(filePath);
            var entries = JsonSerializer.Deserialize<DiagnosticLogEntry[]>(contents);
            if (entries is not null)
            {
                buffer.Replace(entries);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to load Midnight Timeline diagnostics: {exception}");
        }
    }

    private void ThrowIfDisposed(bool allowDispose = false)
    {
        if (disposed && !allowDispose)
        {
            throw new ObjectDisposedException(nameof(PersistentDiagnosticLog));
        }
    }
}
