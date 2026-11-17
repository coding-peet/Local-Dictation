using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

public sealed class PrivateTranscriptPicker : StackPanel
{
    readonly ComboBox selector=new(){SelectedValuePath=nameof(PrivateTranscriptChoice.Id),MaxDropDownHeight=240,Margin=new(0,8,0,4)};
    readonly Button openButton;
    PrivateTranscriptChoice[] choices=[];
    public PrivateTranscriptPicker(Action<string> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        Visibility=Visibility.Collapsed;
        AutomationProperties.SetName(selector,"Private transcripts");
        HintAssist.SetHint(selector,"Private transcripts");HintAssist.SetIsFloating(selector,true);
        var label=new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty,new Binding(nameof(PrivateTranscriptChoice.Label)));
        label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.NoWrap);
        label.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        selector.ItemTemplate=new DataTemplate{VisualTree=label};
        Children.Add(selector);
        openButton=Ui.Button("Open private transcript",()=>
        {
            if(openButton?.IsEnabled==true&&selector.SelectedValue is string id)open(id);
        });
        openButton.HorizontalAlignment=HorizontalAlignment.Left;Children.Add(openButton);
    }
    public void Refresh(IReadOnlyList<PrivateTranscriptChoice> current,bool canOpen)
    {
        ArgumentNullException.ThrowIfNull(current);
        if(!choices.SequenceEqual(current))
        {
            var selected=selector.SelectedValue as string;
            choices=current.ToArray();selector.ItemsSource=choices;
            selector.SelectedValue=choices.FirstOrDefault(c=>c.Id==selected)?.Id??choices.FirstOrDefault()?.Id;
        }
        Visibility=choices.Length>0?Visibility.Visible:Visibility.Collapsed;
        selector.Visibility=choices.Length>1?Visibility.Visible:Visibility.Collapsed;
        selector.IsEnabled=canOpen;
        openButton.IsEnabled=canOpen&&choices.Length>0;
    }
}