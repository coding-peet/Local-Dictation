using System.Windows.Controls;
using System.Windows.Input;

namespace LocalDictation.DeliveryProbe;

// Synthetic Outlook-like paragraph behavior in this fixture's own TextBox only.
// The ordinary native paste completes first. This synchronous handler then
// coalesces one of two leading line feeds beyond the inserted payload.
sealed class ParagraphProjectionTextBox : TextBox
{
    public bool MutateSignature {get;init;}
    public int PasteCommands {get;private set;}
    public int CompleteNativePastes {get;private set;}
    public int CoalescedLineFeeds {get;private set;}
    public string FullPasteText {get;private set;}="";
    public int FullPasteCaret {get;private set;}

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if(e.Key!=Key.V||(Keyboard.Modifiers&ModifierKeys.Control)==0)
        {base.OnPreviewKeyDown(e);return;}
        e.Handled=true;PasteCommands++;
        var before=Text;var start=SelectionStart;var selected=SelectionLength;
        var payload=System.Windows.Clipboard.GetText();
        Paste();
        FullPasteText=Text;FullPasteCaret=SelectionStart;
        if(start!=0||selected!=0||!before.StartsWith("\n\n",StringComparison.Ordinal)||
            FullPasteText!=payload+before||FullPasteCaret!=payload.Length||SelectionLength!=0)return;
        CompleteNativePastes++;
        Text=FullPasteText.Remove(payload.Length,1)+(MutateSignature?"!":"");
        Select(payload.Length,0);CoalescedLineFeeds++;
    }
}
