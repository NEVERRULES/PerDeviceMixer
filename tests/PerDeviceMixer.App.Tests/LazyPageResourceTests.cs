using System.Windows;

namespace PerDeviceMixer.App.Tests;

public sealed class LazyPageResourceTests
{
    [Fact]
    public void PageResourceDictionariesLoadAndInstantiateOnDemand()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application();
                application.Resources.MergedDictionaries.Add(Load("MainWindowResources.xaml"));
                var devices = Load("DevicesPageResources.xaml");
                var settings = Load("SettingsPageResources.xaml");
                application.Resources.MergedDictionaries.Add(devices);
                application.Resources.MergedDictionaries.Add(settings);

                Assert.IsAssignableFrom<FrameworkElement>(
                    Assert.IsType<DataTemplate>(devices["DevicesPageTemplate"]).LoadContent());
                Assert.IsAssignableFrom<FrameworkElement>(
                    Assert.IsType<DataTemplate>(settings["SettingsPageTemplate"]).LoadContent());
                application.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The WPF resource test did not finish.");
        Assert.Null(failure);
    }

    private static ResourceDictionary Load(string name) => new()
    {
        Source = new Uri($"/PerDeviceMixer.App;component/{name}", UriKind.Relative)
    };
}
