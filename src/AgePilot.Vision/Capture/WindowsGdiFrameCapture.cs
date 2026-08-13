using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AgePilot.Vision.Capture;

public sealed class WindowsGdiFrameCapture : IFrameCapture
{
    private const uint DibRgbColors = 0;
    private const uint PrintWindowRenderFullContent = 0x00000002;

    public ValueTask<CapturedFrame> CaptureAsync(
        GameWindow window,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("GDI frame capture requires Windows.");
        }

        if (!GetWindowRect(window.Handle, out var bounds))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read the game window bounds.");
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"Game window has invalid bounds: {width}x{height}.");
        }

        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to acquire a reference device context.");
        }

        var memoryDc = nint.Zero;
        var bitmap = nint.Zero;
        var previousObject = nint.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == nint.Zero || bitmap == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to allocate capture resources.");
            }

            previousObject = SelectObject(memoryDc, bitmap);
            if (previousObject == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to select the capture bitmap.");
            }

            if (!PrintWindow(window.Handle, memoryDc, PrintWindowRenderFullContent))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to render the game window into the capture bitmap.");
            }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                },
            };

            var pixels = new byte[checked(width * height * 4)];
            var scanLines = GetDIBits(
                memoryDc,
                bitmap,
                0,
                (uint)height,
                pixels,
                ref info,
                DibRgbColors);

            if (scanLines != height)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to copy capture pixels.");
            }

            return ValueTask.FromResult(
                new CapturedFrame(width, height, pixels, DateTimeOffset.UtcNow));
        }
        finally
        {
            if (previousObject != nint.Zero && memoryDc != nint.Zero)
            {
                _ = SelectObject(memoryDc, previousObject);
            }

            if (bitmap != nint.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != nint.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(nint.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect bounds);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint SelectObject(nint deviceContext, nint graphicObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint graphicObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint deviceContext, uint flags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        nint deviceContext,
        nint bitmap,
        uint startScan,
        uint scanLines,
        [Out] byte[] bits,
        ref BitmapInfo info,
        uint usage);
}
