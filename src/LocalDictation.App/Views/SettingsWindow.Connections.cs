using System.Windows;
using System.Windows.Controls;

namespace LocalDictation.App.Views;

public sealed partial class SettingsWindow
{
    UIElement ConnectionsPage()
    {
        ConfigureAdvanced(serviceAdvanced);
        var page = new StackPanel { Margin = new(0, 8, 8, 0) };
        var service = new StackPanel();
        service.Children.Add(Ui.Text("Local transcription service", 18, true));
        service.Children.Add(Ui.Row(
            Ui.Button("Reconnect", () => _ = Reconnect()),
            Ui.Button("Start local API", () => _ = controller.StartLocalApi())));

        var advanced = new StackPanel();
        Ui.Label(advanced, "Local service address", endpoint, "The session service supplies your installed models and transcriptions.");
        serviceAdvanced.Content = Ui.SettingsGroup(advanced);
        service.Children.Add(serviceAdvanced);

        page.Children.Add(Section(service));
        return new ScrollViewer
        {
            Content = page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxWidth = 880,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }
}
