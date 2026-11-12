using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LocalDictation.DeliveryProbe;

enum FixtureFocusBehavior { Native,RedirectRedundant,Deferred,Never }

// Synthetic focus behavior only. Native text/caret/paste behavior is unchanged.
sealed class DeferredFocusTextBox : TextBox
{
    readonly TaskCompletionSource restored=new(TaskCreationOptions.RunContinuationsAsynchronously);
    DispatcherTimer? deferred;
    public FixtureFocusBehavior FocusBehavior {get;set;}
    public TextBox? Decoy {get;set;}
    public int AutomationFocusRequests {get;private set;}
    public int RedundantFocusRequests {get;private set;}
    public int DeferredFocusCompletions {get;private set;}
    public int PasteCommands {get;private set;}
    public DateTimeOffset? FocusRequestedAt {get;private set;}
    public DateTimeOffset? FocusRestoredAt {get;private set;}
    public Task FocusRestored=>restored.Task;

    protected override AutomationPeer OnCreateAutomationPeer()=>new Peer(this);
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.V&&(Keyboard.Modifiers&ModifierKeys.Control)!=0)PasteCommands++;
        base.OnPreviewKeyDown(e);
    }
    public void StopDeferredFocus(){deferred?.Stop();}
    bool HandleAutomationFocus()
    {
        AutomationFocusRequests++;FocusRequestedAt??=DateTimeOffset.UtcNow;
        if(FocusBehavior==FixtureFocusBehavior.RedirectRedundant&&IsKeyboardFocusWithin)
        {
            RedundantFocusRequests++;Decoy!.Focus();return true;
        }
        if(FocusBehavior==FixtureFocusBehavior.Never)return true;
        if(FocusBehavior!=FixtureFocusBehavior.Deferred)return false;
        if(deferred!=null)return true;
        deferred=new DispatcherTimer(DispatcherPriority.Normal){Interval=TimeSpan.FromMilliseconds(200)};
        deferred.Tick+=(_,_)=>
        {
            deferred.Stop();Focus();DeferredFocusCompletions++;
            FocusRestoredAt=DateTimeOffset.UtcNow;restored.TrySetResult();
        };
        deferred.Start();return true;
    }
    sealed class Peer(DeferredFocusTextBox owner) : TextBoxAutomationPeer(owner)
    {
        protected override void SetFocusCore()
        {
            if(!owner.HandleAutomationFocus())base.SetFocusCore();
        }
    }
}
