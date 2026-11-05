using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace MobileCap.iOS;

[Register("AppDelegate")]
#pragma warning disable CA1711 // UIKit requires the AppDelegate name
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
