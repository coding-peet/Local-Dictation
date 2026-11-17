using System.Windows;
using System.Windows.Controls;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

public sealed class PreferredLanguagesPanel : WrapPanel
{
    public PreferredLanguagesPanel(IReadOnlyList<string> preferred,IEnumerable<ModelInfo> models,Action<string> remove,Action<string> add)
    {
        var available=LanguagePolicy.Available(models).Where(l=>!preferred.Contains(l.Code)).ToArray();
        foreach(var code in preferred)
        {
            var chip=Ui.Button(code.ToUpperInvariant(),()=>remove(code));chip.Margin=new(0,0,4,0);
            var label=Ui.Text(code.ToUpperInvariant(),14);label.Margin=new(0);
            label.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
            var close=Ui.Text("×",10);close.Margin=new(6,0,0,0);close.VerticalAlignment=VerticalAlignment.Center;
            close.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            chip.Content=Ui.Row(label,close);
            chip.SetResourceReference(FrameworkElement.StyleProperty,"Md3OutlinedButton");
            chip.ToolTip="Remove "+new SpeechLanguage(code).Label+" from Auto";
            System.Windows.Automation.AutomationProperties.SetName(chip,(string)chip.ToolTip);Children.Add(chip);
        }
        var plus=Ui.IconButton(PackIconKind.Plus,"Add preferred language",()=>{});plus.Margin=new(0);
        plus.Click+=(_,_)=>
        {
            var menu=new ContextMenu{MaxHeight=450,PlacementTarget=plus};
            foreach(var value in available)
            {
                var item=new MenuItem{Header=value.Label};item.Click+=(_,_)=>add(value.Code);menu.Items.Add(item);
            }
            if(menu.Items.Count==0)menu.Items.Add(new MenuItem{Header="No other installed model languages",IsEnabled=false});
            plus.ContextMenu=menu;menu.IsOpen=true;
        };
        Children.Add(plus);
    }
}
