using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MobileCap.Views;

namespace MobileCap;

public partial class App : Application
{
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
