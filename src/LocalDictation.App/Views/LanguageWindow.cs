using System.Windows;
using System.Windows.Controls;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

/// <summary>Explicit language correction; recording and inference remain in the existing session pipeline.</summary>
public sealed class LanguageWindow : Window
{
    sealed record Choice(string Code,string Label){public override string ToString()=>Label;}
    public LanguageWindow(AppController controller,string currentModel,bool recording,Func<string,string?> apply,string currentLanguage="auto",IReadOnlyList<string>? preferredLanguages=null)
    {
        Title="Local Dictation · Input language";Width=510;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new StackPanel{Margin=new(24)};
        root.Children.Add(Ui.Text("Input language",20,true));
        root.Children.Add(Ui.Text(recording?"Keep dictating. The selected language will reprocess this same recording after Finish.":
            "Reprocess the original recording. Your previous transcript and any edits stay in History.",14));
        var preferred=preferredLanguages??controller.Settings.PreferredLanguages;
        var available=LanguagePolicy.CorrectionLanguages(controller.Models,preferred);
        var choices=new List<Choice>();
        foreach(var code in available.Count>0?new[]{"auto"}.Concat(available.Select(l=>l.Code)):[])
        {
            var model=LanguagePolicy.RetryModel(controller.Models,currentModel,code);
            if(model==null)continue;
            var label=code=="auto"?preferred.Count>0?"Auto · preferred languages":"Auto · supported languages":new SpeechLanguage(code).Label;
            if(model.Id!=currentModel)label+=" · use "+model.Name;
            choices.Add(new(code,label));
        }
        var language=new ComboBox{ItemsSource=choices,SelectedIndex=0,Margin=new(0,16,0,8),IsTextSearchEnabled=true,MaxDropDownHeight=350};
        language.SelectedItem=choices.FirstOrDefault(c=>c.Code==currentLanguage)??choices.FirstOrDefault();
        language.SetBinding(ToolTipProperty,new System.Windows.Data.Binding("SelectedItem.Label"){Source=language});
        System.Windows.Automation.AutomationProperties.SetName(language,"Language for original recording");root.Children.Add(language);
        var message=Ui.Text(choices.Count==0?"No installed model supports your preferred languages. Update the language tags in Settings.":"",13);message.SetResourceReference(TextBlock.ForegroundProperty,"Warning");root.Children.Add(message);
        var applyButton=Ui.Button(recording?"Use after Finish":"Retranscribe",()=>
        {
            if(language.SelectedItem is not Choice choice)return;
            var error=apply(choice.Code);if(error==null)Close();else message.Text=error;
        },true);
        applyButton.IsEnabled=choices.Count>0;
        var actions=Ui.Row(applyButton,Ui.Button("Cancel",Close));
        actions.HorizontalAlignment=HorizontalAlignment.Right;root.Children.Add(actions);Content=root;
    }
}
