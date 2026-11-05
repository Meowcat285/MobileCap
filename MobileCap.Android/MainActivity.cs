using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace MobileCap.Android;

[Activity(
    Label = "MobileCap",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    ScreenOrientation = ScreenOrientation.Portrait)]
public class MainActivity : AvaloniaMainActivity
{
}
