using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LocalDictation.App;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class PickerKeyboardChecks
{
    const BindingFlags Fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    sealed class DetachedPresentationSource : PresentationSource
    {
        public override Visual RootVisual {get;set;}=null!;
        public override bool IsDisposed=>false;
        protected override CompositionTarget GetCompositionTargetCore()=>null!;
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T found)yield return found;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach(var descendant in Find<T>(child))yield return descendant;
    }
    static Button Action(DependencyObject root,string name)=>Find<Button>(root).Single(button=>AutomationProperties.GetName(button)==name);
    static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static KeyEventArgs Escape(UIElement target)
    {
        var key=new KeyEventArgs(Keyboard.PrimaryDevice,new DetachedPresentationSource(),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};
        target.RaiseEvent(key);return key;
    }
    static ReviewDocument Document()
    {
        var document=new ReviewDocument{PrimaryModel="first",Language="en",RequestedLanguage="en",RequiresExplicitInsert=true};
        document.SetPrimary("Original synthetic wording.");document.ManualFinalDraft="Kept synthetic correction.";return document;
    }
    public static void Run(string output,List<string> checks)
    {
        var failures=new List<string>();
        void Check(string name,Action run)
        {
            try{run();checks.Add(name);}
            catch(Exception error){failures.Add(name+": "+error.Message);}
        }
        Check("inline picker gives its model logical focus without native input",()=>
        {
            var requests=0;
            var picker=new RetranscriptionPicker(()=>[new(){Id="second",Name="Second model",Installed=true}],_=>{requests++;return null;});
            picker.Toggle();
            var choice=Find<ComboBox>(picker).Single();
            Require(picker.Visibility==Visibility.Visible&&choice.SelectedItem is ModelInfo{Id:"second"},"Opening lost its eligible selected model.");
            Require(ReferenceEquals(FocusManager.GetFocusedElement(picker),choice),"Opening did not move logical focus to the model choice.");
            Require(requests==0,"Opening started transcription without confirmation.");
            Require(PresentationSource.FromVisual(picker)==null,"The inline picker created a native source.");
        });
        Check("compact Review Escape cancels picker before closing and restores trigger focus",()=>
        {
            var document=Document();var controller=ReviewWorkflowChecks.Controller(output,document);
            typeof(AppController).GetField("bannerProgress",Fields)!.SetValue(controller,new CompactReviewProgress(document));
            var window=new CompactReviewWindow(controller);
            CheckOwner(window,document);
        });
        Check("Details Escape cancels picker before closing and restores trigger focus",()=>
        {
            var document=Document();
            var window=new ReviewWindow(document,_=>{},_=>throw new InvalidOperationException("Escape dismissed saved choices."),
                _=>throw new InvalidOperationException("Keyboard navigation inserted text."),_=>throw new InvalidOperationException("Keyboard navigation chose a destination."));
            window.SetVerificationAction(()=>[new(){Id="second",Name="Second model",Installed=true}],
                _=>throw new InvalidOperationException("Picker cancellation started transcription."),true);
            CheckOwner(window,document);
        });
        if(failures.Count>0)throw new InvalidOperationException(string.Join(Environment.NewLine,failures));
    }
    static void CheckOwner(Window window,ReviewDocument document)
    {
        var closed=false;window.Closed+=(_,_)=>closed=true;
        try
        {
            var before=JsonSerializer.Serialize(document);
            var trigger=Action(window,"Transcribe again");var picker=Find<RetranscriptionPicker>(window).Single();
            Click(trigger);
            Require(picker.Visibility==Visibility.Visible,"The trigger did not open the picker.");
            var choice=Find<ComboBox>(picker).Single();
            // A detached fixture cannot acquire desktop keyboard focus. Simulate the
            // containing scope's logical focus after normal keyboard focus entered it.
            FocusManager.SetFocusedElement(window,choice);
            var first=Escape(choice);
            Require(first.Handled&&picker.Visibility==Visibility.Collapsed&&!closed,"First Escape did not cancel only the inline picker.");
            Require(ReferenceEquals(FocusManager.GetFocusedElement(window),trigger),"Cancelling the picker did not return logical focus to its trigger.");
            Require(JsonSerializer.Serialize(document)==before,"Cancelling changed the transcript, draft, choices or insertion receipt.");
            Click(trigger);FocusManager.SetFocusedElement(window,Find<ComboBox>(picker).Single());Click(Action(picker,"Cancel"));
            Require(picker.Visibility==Visibility.Collapsed&&!closed&&ReferenceEquals(FocusManager.GetFocusedElement(window),trigger),
                "The Cancel button did not preserve the Review and return logical focus.");
            Require(new WindowInteropHelper(window).Handle==0,"Picker keyboard checks created a native owner window.");
            var second=Escape(trigger);
            Require(second.Handled&&closed,"Escape after cancelling the picker did not close its Review.");
            Require(JsonSerializer.Serialize(document)==before,"Closing changed the saved review document.");
        }
        finally{if(!closed)window.Close();}
    }
}