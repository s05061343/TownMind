using System.Diagnostics;

namespace AgePilot.Vision.Capture;

public sealed class WindowsGameWindowLocator : IGameWindowLocator
{
    private static readonly string[] KnownProcessNames = ["AoE2DE_s", "AoE2DE"];

    public GameWindow? Find()
    {
        foreach (var processName in KnownProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (process.MainWindowHandle != nint.Zero)
                    {
                        return new GameWindow(
                            process.MainWindowHandle,
                            process.MainWindowTitle,
                            process.Id);
                    }
                }
            }
        }

        return null;
    }
}
