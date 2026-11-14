using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalDictation.Core;
namespace LocalDictation.App.Views;
public sealed partial class SettingsWindow
{
    readonly TextBox dictionaryHints=Ui.Box(true,90),dictionaryCorrections=Ui.Box(true,90);
    readonly TextBlock dictionaryStatus=Ui.Text("",12);
    bool dictionaryDirty;
    readonly DispatcherTimer dictionarySaveDelay=new(){Interval=TimeSpan.FromMilliseconds(650)};
    FrameworkElement DictionarySection()
    {
        var panel=new StackPanel();panel.Children.Add(Ui.Text("Personal dictionary",18,true));
        Ui.Label(panel,"Recognition hints",dictionaryHints,"One preferred name or technical term per line. These are hints to the dictation model, not guaranteed spellings. They remain off until the runtime and paired quality evaluation pass.");
        Ui.Label(panel,"Exact corrections",dictionaryCorrections,"One correction per line: heard text => replacement. Matching is case sensitive, uses complete Unicode word/phrase boundaries and runs once, longest match first. Manual edits always take precedence.");
        var config=controller.DictionarySettings;var retained=controller.DictionaryEditorDraft;
        dictionaryHints.Text=retained?.Hints??string.Join(Environment.NewLine,config.Hints);dictionaryCorrections.Text=retained?.Corrections??string.Join(Environment.NewLine,config.Corrections.Select(c=>c.Source+" => "+c.Replacement));dictionaryDirty=retained!=null;
        var hintSwitch=Ui.Switch("Use recognition hints");var correctionSwitch=Ui.Switch("Use exact corrections");hintSwitch.IsEnabled=correctionSwitch.IsEnabled=false;
        panel.Children.Add(Ui.SwitchRow("Use recognition hints",hintSwitch,"Unavailable until paired evaluation is complete."));panel.Children.Add(Ui.SwitchRow("Use exact corrections",correctionSwitch,"Unavailable until paired evaluation is complete."));
        dictionaryStatus.Text=retained==null?controller.PersonalDictionaryStatus:"Unsaved dictionary draft kept in memory. "+(retained.Error??"Edit or retry saving before exiting.");dictionaryStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(dictionaryStatus);
        dictionarySaveDelay.Tick+=(_,_)=>SaveDictionaryEditor();
        foreach(var text in new[]{dictionaryHints,dictionaryCorrections})
        {text.TextChanged+=(_,_)=>{if(loading||updating)return;dictionaryDirty=true;controller.RetainDictionaryDraft(dictionaryHints.Text,dictionaryCorrections.Text);dictionarySaveDelay.Stop();dictionarySaveDelay.Start();};text.LostKeyboardFocus+=(_,_)=>SaveDictionaryEditor();}
        Closing+=(_,_)=>SaveDictionaryEditor();Closed+=(_,_)=>dictionarySaveDelay.Stop();return panel;
    }
    void SaveDictionaryEditor()
    {
        dictionarySaveDelay.Stop();if(loading||updating||!dictionaryDirty)return;
        try
        {
            var hints=dictionaryHints.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>s.Length>0).ToArray();
            var rules=new List<ExactCorrection>();
            foreach(var line in dictionaryCorrections.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries))
            {
                var split=line.IndexOf("=>",StringComparison.Ordinal);if(split<0)throw new ArgumentException("Write each correction as: heard text => replacement.");
                rules.Add(new(line[..split].Trim(),line[(split+2)..].Trim()));
            }
            var error=controller.SavePersonalDictionary(new(hints,rules));
            if(error==null){dictionaryDirty=false;controller.ClearDictionaryDraft();}else controller.RetainDictionaryDraft(dictionaryHints.Text,dictionaryCorrections.Text,error);dictionaryStatus.Text=error??controller.PersonalDictionaryStatus;dictionaryStatus.SetResourceReference(TextBlock.ForegroundProperty,error==null?"Muted":"Warning");
        }
        catch(ArgumentException error){controller.RetainDictionaryDraft(dictionaryHints.Text,dictionaryCorrections.Text,error.Message);dictionaryStatus.Text=error.Message;dictionaryStatus.SetResourceReference(TextBlock.ForegroundProperty,"Warning");}
    }
}
