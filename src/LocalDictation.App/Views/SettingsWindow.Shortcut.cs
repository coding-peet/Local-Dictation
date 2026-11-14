using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

public sealed partial class SettingsWindow
{
    readonly TextBlock shortcutCaptureHint=Ui.Text("",12);
    readonly WrapPanel shortcutKeys=new(){VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock shortcutAction=Ui.Text("",12,true);
    readonly RadioButton shortcutPress=new(){Content="Press to start/stop",GroupName="RecordingShortcutMode",MinHeight=48,Margin=new(0,0,12,0)};
    readonly RadioButton shortcutHold=new(){Content="Hold to record",GroupName="RecordingShortcutMode",MinHeight=48};
    readonly PackIcon shortcutEditIcon=new(){Kind=PackIconKind.Pencil,Width=18,Height=18};
    Button shortcutRecheck=null!;
    ShortcutCapture? shortcutCapture;

    FrameworkElement ShortcutSection()
    {
        var panel=new StackPanel();var heading=new DockPanel{MinHeight=48};
        var enableLabel=Ui.Text("Enable",18,true);enableLabel.Margin=new(0);enableLabel.VerticalAlignment=VerticalAlignment.Center;
        var enabled=Ui.Row(enableLabel,shortcutEnabled);enabled.VerticalAlignment=VerticalAlignment.Center;
        DockPanel.SetDock(enabled,Dock.Right);heading.Children.Add(enabled);
        var about=SettingsHelp.Create("Keyboard shortcut","Press to start/stop toggles recording. Hold to record starts when the full shortcut is down and finishes when a key is released. Modifier-only shortcuts can also overlap other app shortcuts. Wait for dictation to finish before changing its shortcut.");
        DockPanel.SetDock(about,Dock.Right);heading.Children.Add(about);
        var title=Ui.Text("Keyboard shortcut",18,true);title.Margin=new(0);title.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(title);panel.Children.Add(heading);
        AutomationProperties.SetName(shortcutEnabled,"Enable recording shortcut");
        shortcutPress.SetResourceReference(Control.ForegroundProperty,"Ink");shortcutHold.SetResourceReference(Control.ForegroundProperty,"Ink");
        var modes=new WrapPanel();modes.Children.Add(shortcutPress);modes.Children.Add(shortcutHold);panel.Children.Add(modes);
        AutomationProperties.SetName(modes,"Recording shortcut behavior");
        AutomationProperties.SetName(shortcutPress,"Press to start or stop dictation");AutomationProperties.SetName(shortcutHold,"Hold shortcut to record, release to finish");
        var binding=new WrapPanel{Margin=new(0,4,0,0)};
        var label=Ui.Text("Shortcut",12);label.Margin=new(0,0,12,0);label.VerticalAlignment=VerticalAlignment.Center;binding.Children.Add(label);
        shortcutKeys.Margin=new(0,0,4,0);binding.Children.Add(shortcutKeys);binding.Children.Add(shortcutValue);binding.Children.Add(shortcutAction);panel.Children.Add(binding);
        shortcutCaptureHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(shortcutCaptureHint);panel.Children.Add(shortcut);
        shortcutRecheck=Ui.Button("Recheck shortcut",()=>{controller.RecheckShortcut();Refresh();});panel.Children.Add(shortcutRecheck);
        return panel;
    }
    void UpdateShortcutOptions(bool synchronize=true)
    {
        if(synchronize){shortcutPress.IsChecked=controller.Settings.ShortcutMode==ShortcutMode.Toggle;shortcutHold.IsChecked=controller.Settings.ShortcutMode==ShortcutMode.Hold;}
        shortcutPress.IsEnabled=shortcutHold.IsEnabled=controller.Settings.EnableShortcut&&!controller.IsBusy;
        shortcutValue.IsEnabled=controller.Settings.EnableShortcut&&!controller.IsBusy;
        var warning=controller.Settings.EnableShortcut&&(controller.ShortcutMessage.Contains("TypeWhisper")||controller.ShortcutMessage.Contains("disabled")||controller.ShortcutMessage.Contains("release"));
        // A normal Hold hint also contains release; its mode radio already explains it.
        if(controller.ShortcutMessage.StartsWith("Hold "))warning=false;
        shortcut.Visibility=!shortcutCapturing&&warning?Visibility.Visible:Visibility.Collapsed;
        shortcutRecheck.Visibility=warning&&controller.ShortcutMessage.Contains("TypeWhisper")?Visibility.Visible:Visibility.Collapsed;
    }
    void SaveShortcutMode(object sender,RoutedEventArgs e)
    {
        if(loading)return;
        var mode=ReferenceEquals(sender,shortcutHold)?ShortcutMode.Hold:ShortcutMode.Toggle;
        EndShortcutCapture();var value=Snapshot();value.ShortcutMode=mode;
        if(!Persist(value)){loading=true;try{UpdateShortcutOptions();}finally{loading=false;}}
    }
    bool shortcutCapturing;
    string shortcutError="";

    void ConfigureShortcutCapture()
    {
        shortcutValue.SetResourceReference(StyleProperty,"Md3IconButton");
        shortcutValue.Width=shortcutValue.Height=48;shortcutValue.MinHeight=48;shortcutValue.MinWidth=48;
        shortcutValue.Margin=new(0);shortcutValue.Content=shortcutEditIcon;
        shortcutAction.Margin=new(8,0,0,0);shortcutAction.VerticalAlignment=VerticalAlignment.Center;shortcutAction.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
        foreach(var text in new[]{shortcutAction,shortcutCaptureHint})
        {
            var style=new Style(typeof(TextBlock),text.Style);
            style.Triggers.Add(new Trigger{Property=TextBlock.TextProperty,Value="",Setters={new Setter(VisibilityProperty,Visibility.Collapsed)}});text.Style=style;
        }
        shortcutPress.Checked+=SaveShortcutMode;shortcutHold.Checked+=SaveShortcutMode;
        shortcutEnabled.Unchecked+=(_,_)=>EndShortcutCapture();
        shortcutValue.LostKeyboardFocus+=(_,_)=>EndShortcutCapture();
        shortcutValue.PreviewKeyDown+=(_,e)=>
        {
            if(!shortcutCapturing)return;
            var key=e.Key==Key.System?e.SystemKey:e.Key;
            if(key==Key.Tab){EndShortcutCapture();return;}
            e.Handled=true;ProcessShortcutKey(key,true);
        };
        shortcutValue.PreviewKeyUp+=(_,e)=>
        {
            if(!shortcutCapturing)return;
            var key=e.Key==Key.System?e.SystemKey:e.Key;
            if(key==Key.Tab)return;
            e.Handled=true;ProcessShortcutKey(key,false);
        };
        AutomationProperties.SetLiveSetting(shortcutCaptureHint,AutomationLiveSetting.Polite);
        Deactivated+=(_,_)=>EndShortcutCapture();
        Closed+=(_,_)=>EndShortcutCapture();
    }

    void ToggleShortcutCapture(){if(shortcutCapturing)EndShortcutCapture();else BeginShortcutCapture();}
    void BeginShortcutCapture()
    {
        if(shortcutCapturing)return;
        if(!focusShortcutRecorder()){shortcutCaptureHint.Text="Activate this window, then select Change.";return;}
        if(!controller.SetShortcutCapture(true)){shortcutCaptureHint.Text="Wait for dictation to finish before changing its shortcut.";return;}
        shortcutCapturing=true;shortcutCapture=new();shortcutError="";
        shortcutCaptureHint.Text="Press keys, then release to save. Esc cancels.";
        RefreshShortcutPresentation();UpdateSaveStatus();
    }
    void EndShortcutCapture()
    {
        if(!shortcutCapturing)return;
        shortcutCapturing=false;shortcutCapture=null;controller.SetShortcutCapture(false);shortcutError="";
        shortcutCaptureHint.Text="";
        RefreshShortcutPresentation();UpdateSaveStatus();
    }
    void RefreshShortcutPresentation()
    {
        var value=shortcutCapturing?shortcutCapture?.Preview??"":controller.Settings.Shortcut;
        shortcutKeys.Children.Clear();
        if(value.Length==0)
        {
            var prompt=Ui.Text("Press keys…");prompt.Margin=new(0);shortcutKeys.Children.Add(prompt);
        }
        else
        {
            foreach(var name in value.Split('+',StringSplitOptions.RemoveEmptyEntries))
            {
                var part=new StackPanel{Orientation=Orientation.Horizontal};
                if(shortcutKeys.Children.Count>0)
                {var plus=Ui.Text("+",12);plus.Margin=new(6,0,6,0);plus.VerticalAlignment=VerticalAlignment.Center;part.Children.Add(plus);}
                var label=Ui.Text(name,13,true);label.Margin=new(0);label.HorizontalAlignment=HorizontalAlignment.Center;label.VerticalAlignment=VerticalAlignment.Center;
                var cap=new Border{Tag="ShortcutKey",Child=label,CornerRadius=new(4),BorderThickness=new(1),Padding=new(8,3,8,3),MinWidth=32,MinHeight=30};
                cap.SetResourceReference(Border.BackgroundProperty,"SurfaceContainerHigh");cap.SetResourceReference(Border.BorderBrushProperty,"InputBorder");part.Children.Add(cap);shortcutKeys.Children.Add(part);
            }
        }
        shortcut.Text=shortcutCapturing?"Current shortcut: "+controller.Settings.Shortcut:controller.ShortcutMessage;
        shortcutAction.Text=shortcutCapturing?"Listening…":"";
        shortcutEditIcon.Kind=shortcutCapturing?PackIconKind.Close:PackIconKind.Pencil;
        UpdateShortcutOptions(false);

        shortcutValue.ToolTip=shortcutCapturing?"Press a shortcut, or click to cancel.":"Change the recording shortcut";
        AutomationProperties.SetName(shortcutValue,shortcutCapturing?"Recording shortcut: listening. "+value:
            "Change recording shortcut. Current: "+value);
    }
    void ProcessShortcutKey(Key key,bool pressed)
    {
        if(!shortcutCapturing)return;
        if(pressed&&shortcutCapture?.Complete==true){shortcutCapture=new();shortcutError="";UpdateSaveStatus();}
        var result=shortcutCapture!.Update(KeyInterop.VirtualKeyFromKey(key),pressed);
        RefreshShortcutPresentation();
        if(!result.Complete)return;
        if(result.Cancelled){EndShortcutCapture();shortcutCaptureHint.Text="";return;}
        if(result.Shortcut is null){shortcutError=result.Error;UpdateSaveStatus();return;}
        var value=Snapshot();value.Shortcut=result.Shortcut.ToString();
        if(Persist(value)){EndShortcutCapture();shortcutCaptureHint.Text="";}
    }
}
