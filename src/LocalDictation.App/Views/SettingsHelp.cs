using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

/// <summary>Occasional setting explanations, independent of a field's value or status.</summary>
public static class SettingsHelp
{
    static readonly DependencyProperty PopoverProperty=DependencyProperty.RegisterAttached(
        "Popover",typeof(Popup),typeof(SettingsHelp));

    public static Button Create(string label,string helpText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(helpText);
        Popup? popover=null;
        var button=Ui.IconButton(PackIconKind.InformationOutline,"About "+label,()=>
        {
            // Detached render/inspection trees never create a native popup surface.
            if(popover!=null&&PresentationSource.FromVisual(popover.PlacementTarget)!=null)
                popover.IsOpen=!popover.IsOpen;
        });
        button.Content=new PackIcon{Kind=PackIconKind.InformationOutline,Width=18,Height=18};
        button.Margin=new(4,0,0,0);
        button.VerticalAlignment=VerticalAlignment.Center;
        AutomationProperties.SetHelpText(button,helpText);
        button.ToolTip=new ToolTip{Content=HelpText(helpText),MaxWidth=360};

        var copy=new StackPanel();
        var title=Ui.Text(label,14,true);title.Margin=new(0,0,0,8);copy.Children.Add(title);
        copy.Children.Add(new ScrollViewer{Content=HelpText(helpText),MaxHeight=240,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            Focusable=false});
        var surface=new Border{Child=copy,Padding=new(16),MaxWidth=360,BorderThickness=new(1)};
        surface.SetResourceReference(Border.BackgroundProperty,"SurfaceContainerHigh");
        surface.SetResourceReference(Border.BorderBrushProperty,"InputBorder");
        surface.SetResourceReference(Border.CornerRadiusProperty,"ShapeMedium");
        AutomationProperties.SetName(surface,label+" help");
        popover=new Popup{Child=surface,PlacementTarget=button,Placement=PlacementMode.Bottom,
            VerticalOffset=4,StaysOpen=false,AllowsTransparency=true,PopupAnimation=PopupAnimation.Fade,Focusable=false};
        button.SetValue(PopoverProperty,popover);
        button.Click+=(_,args)=>args.Handled=true;
        button.PreviewKeyDown+=(_,args)=>
        {
            if(args.Key==Key.Escape&&popover.IsOpen){popover.IsOpen=false;args.Handled=true;}
        };
        button.LostKeyboardFocus+=(_,_)=>popover.IsOpen=false;
        button.Unloaded+=(_,_)=>popover.IsOpen=false;
        return button;
    }

    /// <summary>The actual popup body, inspectable and renderable without opening a native surface.</summary>
    public static FrameworkElement GetPopoverContent(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return button.GetValue(PopoverProperty) is Popup{Child:FrameworkElement body}
            ?body:throw new ArgumentException("The button is not a settings help control.",nameof(button));
    }

    static TextBlock HelpText(string text)
    {
        var block=Ui.Text(text);block.Margin=new(0);block.MaxWidth=320;return block;
    }
}
