using LocalDictation.Core;
namespace LocalDictation.App;
public sealed record PersonalDictionaryDraft(string Hints,string Corrections,string? Error=null);
public sealed partial class AppController
{
    PersonalDictionarySettings? personalDictionary;
    public PersonalDictionaryDraft? DictionaryEditorDraft {get;private set;}
    public void RetainDictionaryDraft(string hints,string corrections,string? error=null)=>DictionaryEditorDraft=new(hints,corrections,error);
    public void ClearDictionaryDraft()=>DictionaryEditorDraft=null;
    bool ConfirmDictionaryDraftExit()=>System.Windows.MessageBox.Show("Dictionary changes are not saved. Discard this in-memory draft and exit?", "Unsaved dictionary",System.Windows.MessageBoxButton.YesNo,System.Windows.MessageBoxImage.Warning,System.Windows.MessageBoxResult.No)==System.Windows.MessageBoxResult.Yes;
    PersonalDictionaryStore? dictionaryStore;
    PersonalDictionaryStore DictionaryStore=>dictionaryStore??=new(AppPaths.Data);
    public string PersonalDictionaryStatus {get;private set;}="Recognition hints and exact corrections are off until paired evaluation is complete.";
    public PersonalDictionarySettings DictionarySettings
    {
        get
        {
            if(personalDictionary!=null)return personalDictionary;
            try{personalDictionary=DictionaryStore.Load();}
            catch(Exception){PersonalDictionaryStatus="The saved dictionary could not be read. Its file was preserved; recognition and corrections remain off.";personalDictionary=new([],[]);}
            return personalDictionary;
        }
    }
    public string? SavePersonalDictionary(PersonalDictionarySettings value)
    {
        if(IsBusy)return "Finish the current operation before changing the dictionary.";
        try{DictionaryStore.Save(value);personalDictionary=value;PersonalDictionaryStatus="Dictionary saved. Recognition hints and exact corrections remain off until paired evaluation is complete.";return null;}
        catch(ArgumentException error){return error.Message;}
        catch(Exception){return "The dictionary could not be saved. The previous protected version was preserved.";}
    }
}
