using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LocalDictation.Core;

namespace LocalDictation.App.Views;

/// <summary>Inline model selection; never creates or activates another window.</summary>
public sealed class RetranscriptionPicker : Border
{
    public event Action? Changed;
    public FrameworkElement? ReturnFocusTarget {get;set;}
    readonly Func<IReadOnlyList<ModelInfo>> models;
    readonly Func<ModelInfo,string?> transcribe;
    readonly ComboBox choice=new(){DisplayMemberPath="Label",Margin=new(0,4,0,8)};
    readonly Button run;
    readonly TextBlock message=Ui.Text("",12);
    public RetranscriptionPicker(Func<IReadOnlyList<ModelInfo>> models,Func<ModelInfo,string?> transcribe)
    {
        this.models=models;this.transcribe=transcribe;Visibility=Visibility.Collapsed;
        FocusManager.SetIsFocusScope(this,true);
        Margin=new(0,12,0,0);Padding=new(12);SetResourceReference(BackgroundProperty,"SurfaceContainer");
        SetResourceReference(CornerRadiusProperty,"ShapeMedium");
        var root=new StackPanel();root.Children.Add(Ui.Text("Transcribe again",14,true));
        root.Children.Add(Ui.Text("Use the original recording with another model. Your current text is kept.",12));
        root.Children.Add(Ui.Text("Model",12,true));
        System.Windows.Automation.AutomationProperties.SetName(choice,"Transcription model");
        root.Children.Add(choice);
        run=Ui.Button("Transcribe",()=>
        {
            if(choice.SelectedItem is not ModelInfo model)return;
            var error=transcribe(model);
            if(error==null){message.Text="";ClosePicker();}
            else message.Text=error;
        },true);
        root.Children.Add(Ui.Row(run,Ui.Button("Cancel",ClosePicker)));
        message.SetResourceReference(TextBlock.ForegroundProperty,"Warning");root.Children.Add(message);Child=root;
    }
    public void Toggle()
    {
        if(Cancel())return;
        Visibility=Visibility.Visible;message.Text="";Refresh(true);
        Changed?.Invoke();
        // Remember the selected field in this focus scope. Request keyboard
        // focus after the owning Review has revealed the picker.
        FocusManager.SetFocusedElement(this,choice);
        Dispatcher.BeginInvoke(DispatcherPriority.Input,(Action)(()=>
        {
            if(Visibility==Visibility.Visible&&choice.IsVisible&&choice.IsEnabled)choice.Focus();
        }));
    }
    public bool Cancel()
    {
        if(Visibility!=Visibility.Visible)return false;
        ClosePicker();return true;
    }
    void ClosePicker()
    {
        Visibility=Visibility.Collapsed;Changed?.Invoke();
        if(ReturnFocusTarget is not {IsEnabled:true} target)return;
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target),target);
        if(target.IsVisible)target.Focus();
    }
    public void Refresh(bool enabled)
    {
        var selected=(choice.SelectedItem as ModelInfo)?.Id;
        var available=models();
        choice.ItemsSource=available;
        choice.SelectedItem=available.FirstOrDefault(m=>m.Id==selected)??available.FirstOrDefault();
        choice.IsEnabled=enabled;run.IsEnabled=enabled&&available.Count>0;
    }
}
