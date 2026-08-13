using System.Windows;
using System.Windows.Threading;
using AgePilot.Infrastructure.Diagnostics;

namespace AgePilot.App;

internal sealed class GlobalExceptionMiddleware : IDisposable
{
    private readonly System.Windows.Application _application;
    private readonly LocalJsonLineLogger _logger;
    private readonly string _mode;
    private bool _disposed;

    private GlobalExceptionMiddleware(System.Windows.Application application, LocalJsonLineLogger logger, string mode)
    {
        _application = application;
        _logger = logger;
        _mode = mode;
        _application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _logger.Write("application.started", Context("startup"));
    }

    public static GlobalExceptionMiddleware Install(System.Windows.Application application, LocalJsonLineLogger logger, string mode) =>
        new(application, logger, mode);

    public void ReportCaught(string source, Exception exception) =>
        _logger.WriteException("exception.caught", exception, Context(source));

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        _logger.WriteException("exception.dispatcher_unhandled", args.Exception, Context("WPF Dispatcher"));
        try
        {
            System.Windows.MessageBox.Show(
                $"AgePilot 發生未處理錯誤，將停止以避免繼續操作。\n\n{args.Exception.Message}\n\n診斷：{_logger.Path}",
                "AgePilot 錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
        args.Handled = false;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var exception = args.ExceptionObject as Exception ?? new InvalidOperationException(args.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
        _logger.WriteException("exception.domain_unhandled", exception,
            new { context = Context("AppDomain"), args.IsTerminating });
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        _logger.WriteException("exception.task_unobserved", args.Exception, Context("TaskScheduler"));
        args.SetObserved();
    }

    private object Context(string source) => new
    {
        source,
        mode = _mode,
        processId = Environment.ProcessId,
        threadId = Environment.CurrentManagedThreadId,
        processPath = Environment.ProcessPath,
        commandLine = Environment.CommandLine,
        os = Environment.OSVersion.VersionString,
        runtime = Environment.Version.ToString(),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _application.DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _logger.Write("application.stopped", Context("shutdown"));
    }
}
