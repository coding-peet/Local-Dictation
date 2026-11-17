using System.Windows;
using System.Windows.Media;
using LocalDictation.Core;

namespace LocalDictation.App;

public partial class App : System.Windows.Application
{
    Mutex? mutex;
    AppController? controller;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var launch = WindowsStartupLaunchPolicy.FromArguments(e.Args);
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender,_)=>{if(sender is Window window)Services.ThemeManager.TitleBar(window);}));
        mutex = new Mutex(true, @"Local\LocalDictation.NativeClient", out var created);
        if (!created)
        {
            if (launch.ShowAlreadyRunningMessage)
                MessageBox.Show("Local Dictation is already running. Open it from the system tray.","Local Dictation");
            Shutdown(); return;
        }
        Services.DeliveryDiagnostic.WriteSessionIfEnabled();
        if (SystemParameters.HighContrast)
        {
            foreach(var key in new[]{"Canvas","Surface"}) Resources[key]=SystemColors.WindowBrush;
            foreach(var key in new[]{"Ink","Muted"}) Resources[key]=SystemColors.WindowTextBrush;
            Resources["Accent"]=SystemColors.HighlightBrush; Resources["Selected"]=SystemColors.HighlightBrush;
            Resources["InputBorder"]=SystemColors.WindowTextBrush;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled=true;
            controller?.EmergencyReview();
            MessageBox.Show("An operation failed. Check History and General. If protected saving failed, copy the text before closing.",
                            "Local Dictation",MessageBoxButton.OK,MessageBoxImage.Warning);
        };
        try
        {
            controller=new AppController(e.Args.Contains("--no-hotkey"));
            if (launch.ShowSettings) controller.ShowSettings();
            _=controller.InitializeConnection();
        }
        catch(Exception)
        {
            MessageBox.Show("Local Dictation could not initialize. Check the data folder and Windows access.",
                            "Local Dictation",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        controller?.Dispose();
        mutex?.Dispose();
        base.OnExit(e);
    }
}
