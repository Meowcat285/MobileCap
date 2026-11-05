using System.Runtime.InteropServices;
using ARKit;
using Foundation;
using MobileCap.Core;
using MobileCap.Core.Capture;
using MobileCap.Platform;

namespace MobileCap.iOS.ArKit;

internal sealed class ArKitPlatform : IArPlatform
{
    public string? UnavailableReason { get; } =
        ARWorldTrackingConfiguration.IsSupported ? null : "This device does not support ARKit world tracking.";

    public DeviceInfo Device { get; } = new("Apple", ModelName(), "MobileCap " + Version());

    /// <summary>The app's Documents folder, which the Files app shows (UIFileSharingEnabled).</summary>
    public string CapturesDirectory { get; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public IArCapture Open(CaptureSettings settings) => new ArKitCapture(settings);

    private static string Version() =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "";

    /// <summary>
    /// The name the Camera app writes to EXIF Model, which Spirula groups cameras and looks up
    /// sensor widths by; the raw identifier for phones not listed.
    /// </summary>
    private static string ModelName()
    {
        var id = Machine();
        return id switch
        {
            "iPhone13,1" => "iPhone 12 mini",
            "iPhone13,2" => "iPhone 12",
            "iPhone13,3" => "iPhone 12 Pro",
            "iPhone13,4" => "iPhone 12 Pro Max",
            "iPhone14,4" => "iPhone 13 mini",
            "iPhone14,5" => "iPhone 13",
            "iPhone14,2" => "iPhone 13 Pro",
            "iPhone14,3" => "iPhone 13 Pro Max",
            "iPhone14,6" => "iPhone SE (3rd generation)",
            "iPhone14,7" => "iPhone 14",
            "iPhone14,8" => "iPhone 14 Plus",
            "iPhone15,2" => "iPhone 14 Pro",
            "iPhone15,3" => "iPhone 14 Pro Max",
            "iPhone15,4" => "iPhone 15",
            "iPhone15,5" => "iPhone 15 Plus",
            "iPhone16,1" => "iPhone 15 Pro",
            "iPhone16,2" => "iPhone 15 Pro Max",
            "iPhone17,3" => "iPhone 16",
            "iPhone17,4" => "iPhone 16 Plus",
            "iPhone17,1" => "iPhone 16 Pro",
            "iPhone17,2" => "iPhone 16 Pro Max",
            "iPhone17,5" => "iPhone 16e",
            _ => id,
        };
    }

    private static string Machine()
    {
        nint size = 0;
        if (sysctlbyname("hw.machine", 0, ref size, 0, 0) != 0 || size <= 0) return "iPhone";
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            return sysctlbyname("hw.machine", buffer, ref size, 0, 0) == 0
                ? Marshal.PtrToStringAnsi(buffer) ?? "iPhone"
                : "iPhone";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport(ObjCRuntime.Constants.SystemLibrary)]
    private static extern int sysctlbyname(string name, nint oldp, ref nint oldlenp, nint newp, nint newlen);
}
