using System.Windows;
using System.Windows.Controls;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

public sealed class VerificationWindow : Window
{
    public VerificationWindow(IReadOnlyList<ModelInfo> models,string? selected,Func<ModelInfo,string?> apply)
    {
        Title="Local Dictation · Second transcript";Width=510;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new StackPanel{Margin=new(24)};root.Children.Add(Ui.Text("Second transcript",20,true));
        root.Children.Add(Ui.Text("Compare the original recording with another local model. Your existing transcript and edits are kept.",14));
        var choice=new ComboBox{ItemsSource=models,DisplayMemberPath="Label",Margin=new(0,16,0,8)};
        choice.SelectedItem=models.FirstOrDefault(m=>m.Id==selected)??models.FirstOrDefault();
        System.Windows.Automation.AutomationProperties.SetName(choice,"Second transcription model");root.Children.Add(choice);
        var message=Ui.Text("",13);message.SetResourceReference(TextBlock.ForegroundProperty,"Warning");root.Children.Add(message);
        var run=Ui.Button("Run second transcript",()=>
        {
            if(choice.SelectedItem is not ModelInfo model)return;
            var error=apply(model);if(error==null)Close();else message.Text=error;
        },true);run.IsEnabled=models.Count>0;
        var actions=Ui.Row(Ui.Button("Cancel",Close),run);actions.HorizontalAlignment=HorizontalAlignment.Right;
        root.Children.Add(actions);Content=root;
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;Close();}};
    }
}
