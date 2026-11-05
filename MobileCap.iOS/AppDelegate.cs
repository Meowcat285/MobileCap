using Avalonia;
using Avalonia.iOS;
using Foundation;
using MobileCap.iOS.ArKit;

namespace MobileCap.iOS;

[Register("AppDelegate")]
#pragma warning disable CA1711 // UIKit requires the AppDelegate name
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.Ar = new ArKitPlatform();
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
