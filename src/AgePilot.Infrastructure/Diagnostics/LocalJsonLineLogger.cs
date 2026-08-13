using System.Diagnostics;
using System.Text.Json;

namespace AgePilot.Infrastructure.Diagnostics;

public sealed class LocalJsonLineLogger(string path)
{
    private const long MaximumBytes = 5 * 1024 * 1024;
    private readonly object _sync = new();

    public string Path { get; } = path;
    public string EmergencyPath => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Path) ?? System.IO.Path.GetTempPath(), "agepilot-emergency.log");

    public void Write(string eventName, object? data = null)
    {
        var line = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            eventName,
            processId = Environment.ProcessId,
            threadId = Environment.CurrentManagedThreadId,
            data,
        });
        lock (_sync)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                RotateIfNeeded();
                File.AppendAllText(Path, line + Environment.NewLine);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                WriteEmergency(line, exception);
            }
        }
    }

    public void WriteException(string eventName, Exception exception, object? context = null) => Write(eventName, new
    {
        exceptionType = exception.GetType().FullName,
        exception.Message,
        exception.HResult,
        stackTrace = exception.StackTrace,
        fullException = exception.ToString(),
        innerException = exception.InnerException?.ToString(),
        context,
    });

    private void WriteEmergency(string originalLine, Exception loggingFailure)
    {
        var emergency = $"{DateTimeOffset.UtcNow:O} LOGGER_FAILURE pid={Environment.ProcessId} " +
                        $"{loggingFailure.GetType().FullName}: {loggingFailure.Message}{Environment.NewLine}" +
                        originalLine + Environment.NewLine;
        Debug.WriteLine(emergency);
        try
        {
            var directory = System.IO.Path.GetDirectoryName(EmergencyPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(EmergencyPath, emergency);
        }
        catch (Exception fallbackFailure) when (fallbackFailure is UnauthorizedAccessException or IOException)
        {
            Debug.WriteLine($"AgePilot emergency logging failed: {fallbackFailure}");
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
