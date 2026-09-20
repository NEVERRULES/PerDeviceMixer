using System.Windows;
using System.Windows.Controls;

namespace PerDeviceMixer.App.Tests;

public sealed class LazyPageResourceTests
{
    [Fact]
    public void PageResourceDictionariesLoadInstantiateAndLayoutOnDemand()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                HeadphoneBatteryCoordinatorTests.VerifyToastWindowLifecycle(application);
                application.Resources.MergedDictionaries.Add(Load("MainWindowResources.xaml"));
                var devices = Load("DevicesPageResources.xaml");
                var settings = Load("SettingsPageResources.xaml");
                var tray = Load("TrayMenuResources.xaml");
                application.Resources.MergedDictionaries.Add(devices);
                application.Resources.MergedDictionaries.Add(settings);
                application.Resources.MergedDictionaries.Add(tray);

                Assert.IsAssignableFrom<FrameworkElement>(
                    Assert.IsType<DataTemplate>(devices["DevicesPageTemplate"]).LoadContent());
                var settingsPage = Assert.IsAssignableFrom<FrameworkElement>(
                    Assert.IsType<DataTemplate>(settings["SettingsPageTemplate"]).LoadContent());
                settingsPage.DataContext = new SettingsBindingSource();
                settingsPage.Measure(new Size(1000, 2000));
                settingsPage.Arrange(new Rect(settingsPage.DesiredSize));
                settingsPage.UpdateLayout();

                var trayMenu = new ContextMenu
                {
                    Style = Assert.IsType<Style>(tray["TrayContextMenuStyle"])
                };
                trayMenu.ApplyTemplate();
                var trayMenuRoot = Assert.IsType<Border>(trayMenu.Template.LoadContent());
                Assert.IsType<ItemsPresenter>(trayMenuRoot.Child);
                application.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The WPF resource test did not finish.");
        Assert.Null(failure);
    }

    private static ResourceDictionary Load(string name) => new()
    {
        Source = new Uri($"/PerDeviceMixer.App;component/{name}", UriKind.Relative)
    };

    private sealed class SettingsBindingSource
    {
        private readonly int _progress = 50;

        public int UpdateDownloadProgress => _progress;
    }
}
