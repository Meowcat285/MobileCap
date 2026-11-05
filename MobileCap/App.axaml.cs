using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MobileCap.Platform;
using MobileCap.Views;

namespace MobileCap;

public partial class App : Application
{
    /// <summary>Replaced by a head that has AR before the app starts.</summary>
    public static IArPlatform Ar { get; set; } = new NoArPlatform(
        "AR capture needs an iPhone or iPad. Android support is not built yet.",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MobileCap"));

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow();
                break;
            case IActivityApplicationLifetime activity:
                activity.MainViewFactory = () => new PageNavigationHost { Page = new MainView() };
                break;
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new PageNavigationHost { Page = new MainView() };
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
