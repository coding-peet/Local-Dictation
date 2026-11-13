using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Data;
using System.Globalization;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

/// <summary>Circular endcaps from the visible layer, never from a stretched cached drawing.</summary>
public sealed class CapsuleCornerConverter : IMultiValueConverter
{
    public object Convert(object[] values,Type targetType,object parameter,CultureInfo culture)
    {
        var width=values.ElementAtOrDefault(0) is double w&&double.IsFinite(w)?w:0;
        var height=values.ElementAtOrDefault(1) is double h&&double.IsFinite(h)?h:0;
        return new CornerRadius(Math.Max(0,Math.Min(width,height)/2));
    }
    public object[] ConvertBack(object value,Type[] targetTypes,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}

static class Ui
{
    public static void BindCapsule(Border layer)
    {
        var radius=new MultiBinding{Converter=(IMultiValueConverter)System.Windows.Application.Current.FindResource("CapsuleCornerConverter")};
        radius.Bindings.Add(new Binding(nameof(FrameworkElement.ActualWidth)){Source=layer});
        radius.Bindings.Add(new Binding(nameof(FrameworkElement.ActualHeight)){Source=layer});
        layer.SetBinding(Border.CornerRadiusProperty,radius);
    }
    public static Brush Brush(string key)=>(Brush)System.Windows.Application.Current.FindResource(key);
    public static TextBlock Text(string text,double size=14,bool bold=false)
    {
        var block=new TextBlock{Text=text,Margin=new(0,0,0,8),TextWrapping=TextWrapping.Wrap};
        block.SetResourceReference(FrameworkElement.StyleProperty,size>=24?"MaterialDesignHeadlineSmallTextBlock":size>=20?"MaterialDesignTitleLargeTextBlock":bold?"MaterialDesignTitleMediumTextBlock":size<=12?"MaterialDesignBodySmallTextBlock":"MaterialDesignBodyMediumTextBlock");
        block.SetResourceReference(TextBlock.ForegroundProperty,"Ink");return block;
    }
    // Keep the toolkit's real button template, state layers, ripple and automation behavior.
    // Its visual body is inset inside a rectangular 48-DIP control that receives input.
    sealed class DesktopButton : Button
    {
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if(VisualTreeHelper.GetChildrenCount(this)==0)return;
            var root=VisualTreeHelper.GetChild(this,0);
            if(root is Panel target)target.Background=Brushes.Transparent;
            if(GetTemplateChild("border") is Border body){body.Margin=new(4);BindCapsule(body);}
            InsetLayers(root);
        }
        void InsetLayers(DependencyObject root)
        {
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {
                var child=VisualTreeHelper.GetChild(root,i);
                if(child is FrameworkElement layer&&ReferenceEquals(layer.TemplatedParent,this)&&child is Ripple or ProgressBar)
                    layer.Margin=new(4);
                else InsetLayers(child);
            }
        }
    }

    public static Button Button(string label,Action action,bool primary=false)
    {
        var button=new DesktopButton{Content=label};
        button.SetResourceReference(FrameworkElement.StyleProperty,primary?"Md3FilledButton":"Md3TextButton");
        System.Windows.Automation.AutomationProperties.SetName(button,label.Replace("_",""));
        button.Click+=(_,_)=>action();return button;
    }
    public static Button IconButton(PackIconKind icon,string label,Action action)
    {
        var button=Button(label,action);button.Content=new PackIcon{Kind=icon,Width=20,Height=20};
        button.SetResourceReference(FrameworkElement.StyleProperty,"Md3IconButton");
        button.ToolTip=label;return button;
    }
    public static ToggleButton Switch(string label)
    {
        var control=new ToggleButton{Margin=new(0),VerticalAlignment=VerticalAlignment.Center};
        control.SetResourceReference(FrameworkElement.StyleProperty,"MaterialDesignSwitchToggleButton");
        control.SetResourceReference(Control.FocusVisualStyleProperty,"Md3FocusVisual");
        System.Windows.Automation.AutomationProperties.SetName(control,label);return control;
    }
    public static Grid SwitchRow(string label,ToggleButton toggle,string? hint=null)
    {
        var row=new Grid{Margin=new(0,8,0,8),MinHeight=48};
        row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,16,0)};
        var title=Text(label);title.Margin=new(0);copy.Children.Add(title);
        if(hint!=null){var detail=Text(hint,12);detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");detail.Margin=new(0,4,0,0);copy.Children.Add(detail);}
        copy.Background=Brushes.Transparent;copy.MouseLeftButtonUp+=(_,_)=>{if(toggle.IsEnabled){toggle.IsChecked=toggle.IsChecked!=true;toggle.Focus();}};
        row.Children.Add(copy);Grid.SetColumn(toggle,1);row.Children.Add(toggle);return row;
    }
    public static StackPanel Row(params UIElement[] children)
    {var row=new StackPanel{Orientation=Orientation.Horizontal};foreach(var child in children)row.Children.Add(child);return row;}
    public static Border Card(UIElement content)
    {
        var card=new Border{Child=content,Padding=new(24),Margin=new(0,0,0,16)};
        card.SetResourceReference(Border.CornerRadiusProperty,"ShapeMedium");
        card.SetResourceReference(Border.BackgroundProperty,"SurfaceContainer");return card;
    }
    public static void Label(StackPanel parent,string label,UIElement control,string? hint=null)
    {
        // Outlined toolkit fields keep their real floating hint, validation and focus behavior.
        if(control is TextBox or ComboBox)
        {
            HintAssist.SetHint(control,label);HintAssist.SetIsFloating(control,true);
            if(control is FrameworkElement field)field.Margin=new(0,8,0,8);
        }
        else{var caption=Text(label,12,true);caption.Margin=new(0,8,0,8);parent.Children.Add(caption);}
        parent.Children.Add(control);
        if(hint!=null){var text=Text(hint,12);text.SetResourceReference(TextBlock.ForegroundProperty,"Muted");text.Margin=new(16,0,0,16);parent.Children.Add(text);}
        System.Windows.Automation.AutomationProperties.SetName(control,label);
    }
    public static TextBox Box(bool editable=true,double height=80)=>new()
    {IsReadOnly=!editable,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=height,MaxHeight=height*2,VerticalContentAlignment=VerticalAlignment.Top};
}
