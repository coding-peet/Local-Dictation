using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LocalDictation.App.Views;

static class Ui
{
    public static Brush Brush(string key) => (Brush)System.Windows.Application.Current.FindResource(key);
    public static TextBlock Text(string text, double size = 14, bool bold = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = new(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap
    };
    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label };
        System.Windows.Automation.AutomationProperties.SetName(button, label.Replace("_", ""));
        if (primary)
        {
            button.SetResourceReference(Control.BackgroundProperty,"Accent");
            button.SetResourceReference(Control.ForegroundProperty,"OnAccent");
            button.SetResourceReference(Control.BorderBrushProperty,"Accent");
        }
        button.Click += (_, _) => action();
        return button;
    }
    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }
    public static Border Card(UIElement content)
    {
        var card=new Border {Child=content,BorderThickness=new(1),CornerRadius=new(5),Padding=new(16),Margin=new(0,0,0,16)};
        card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Border");return card;
    }
    public static void Label(StackPanel parent, string label, UIElement control, string? hint = null)
    {
        var caption=Text(label,12,true);caption.Margin=new(0,10,0,7);parent.Children.Add(caption);
        parent.Children.Add(control);
        if (hint != null)
        {
            var text = Text(hint,12); text.SetResourceReference(TextBlock.ForegroundProperty,"Muted"); text.Margin = new(0,7,0,16);
            parent.Children.Add(text);
        }
        else if (control is FrameworkElement element) element.Margin = new(0,0,0,10);
        System.Windows.Automation.AutomationProperties.SetName(control, label);
    }
    public static TextBox Box(bool editable = true, double height = 76) => new()
    {
        IsReadOnly = !editable, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = height,
        MaxHeight = height * 2, VerticalContentAlignment = VerticalAlignment.Top
    };
}
