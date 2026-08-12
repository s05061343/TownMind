using System.Text.Json;

namespace AgePilot.Infrastructure.Diagnostics;

public sealed class LocalJsonLineLogger(string path)
{
    private const long MaximumBytes = 5 * 1024 * 1024;
    private readonly object _sync = new();

    public string Path { get; } = path;

    public void Write(string eventName, object? data = null)
    {
        var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, eventName, data });
        lock (_sync)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                RotateIfNeeded();
                File.AppendAllText(Path, line + Environment.NewLine);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(Path) || new FileInfo(Path).Length < MaximumBytes) return;
        var previous = Path + ".1";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(Path, previous);
    }

    public static LocalJsonLineLogger CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new LocalJsonLineLogger(System.IO.Path.Combine(appData, "AgePilot", "logs", "agepilot.jsonl"));
    }
}
