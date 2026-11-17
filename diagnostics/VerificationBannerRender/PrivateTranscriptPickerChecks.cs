using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDictation.App;
using LocalDictation.App.Views;
using LocalDictation.App.Services;
using LocalDictation.Core;

public static class PrivateTranscriptPickerChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static IEnumerable<T> Find<T>(DependencyObject parent)where T:DependencyObject
    {if(parent is T found)yield return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(parent,i)))yield return child;}
    static ReviewDocument Document(string id,DateTimeOffset created,string text,bool isPrivate=true)
    {var document=new ReviewDocument{Id=id,Created=created,IsPrivate=isPrivate};document.SetPrimary(text);return document;}
    static PropertyInfo ChoicesProperty()
    {
        var property=typeof(AppController).GetProperty("PrivateReviewChoices");
        Require(property!=null,"Private transcripts need immutable ID/date/preview choices for selecting an older pending transcript.");return property!;
    }
    static object Choices(AppController owner)=>ChoicesProperty().GetValue(owner)!;
    static List<object> Items(object choices)=>((IEnumerable)choices).Cast<object>().ToList();
    static string Id(object item)=>(string)item.GetType().GetProperty("Id")!.GetValue(item)!;
    static string Preview(object item)=>(string)item.GetType().GetProperty("Preview")!.GetValue(item)!;
    static void Refresh(FrameworkElement picker,AppController owner,bool canOpen=true)=>picker.GetType().GetMethod("Refresh")!.Invoke(picker,[Choices(owner),canOpen]);
    static FrameworkElement Picker(Action<string> open)
    {
        var type=typeof(SettingsWindow).Assembly.GetType("LocalDictation.App.Views.PrivateTranscriptPicker");
        Require(type!=null,"Private transcripts need a reusable selector whose explicit Open action can be checked without a native Review window.");
        return (FrameworkElement)Activator.CreateInstance(type!,open)!;
    }
    static void Open(AppController owner,string id)
    {
        var method=typeof(AppController).GetMethod("OpenPrivateReview",[typeof(string)]);
        Require(method!=null,"Private transcript opening must resolve the selected ID against current private memory.");
        // These guard fixtures deliberately have no banner: an incorrect dispatch
        // fails at banner.Refresh before it can construct or Show a native window.
        method!.Invoke(owner,[id]);
    }
    static object BusyRun()=>RuntimeHelpers.GetUninitializedObject(typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!);
    static ReviewWindow ApplyingDetails()
    {
        var details=(ReviewWindow)RuntimeHelpers.GetUninitializedObject(typeof(ReviewWindow));
        Set(details,"<IsApplying>k__BackingField",true);return details;
    }
    static SettingsWindow Window(AppController owner)=>(SettingsWindow)typeof(SettingsWindow).GetConstructor(Fields,null,[typeof(AppController),typeof(bool)],null)!.Invoke([owner,false]);
    static Dictionary<string,string> Files(string directory)=>Directory.Exists(directory)?Directory.GetFiles(directory,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(directory,p),p=>Convert.ToBase64String(File.ReadAllBytes(p))):new();
    static void SameFiles(Dictionary<string,string> original,string directory)
    {var current=Files(directory);Require(original.Count==current.Count&&original.All(pair=>current.TryGetValue(pair.Key,out var bytes)&&bytes==pair.Value),"Private selection, editing or dismissal changed protected fixture files.");}
    public static void Run(string output,List<string> checks)
    {
        ChoicesProperty();
        var fixture=Path.Combine(output,"private-selector-fixture-"+Guid.NewGuid().ToString("N"));
        var date=new DateTimeOffset(2026,11,17,8,0,0,TimeSpan.Zero);
        var saved=Document("10000000000000000000000000000000",date,"Saved ordinary transcript",false);
        var owner=ReviewWorkflowChecks.Controller(output,saved);Set(owner,"noHotkey",true);
        var pending=new PendingReviews(new ReviewStore(Path.Combine(fixture,"pending")));Require(pending.Save(saved),"Cannot create isolated protected-text fixture.");
        Set(owner,"pending",pending);Set(owner,"historyStore",new HistoryStore(Path.Combine(fixture,"history")));
        Set(owner,"dictionaryStore",new PersonalDictionaryStore(Path.Combine(fixture,"dictionary")));
        var originalHistory=JsonSerializer.Serialize(owner.History);var originalPending=JsonSerializer.Serialize(owner.Pending);var originalFiles=Files(fixture);
        var settingsPath=Path.Combine(AppPaths.Data,"settings.json");var originalSettings=File.Exists(settingsPath)?Convert.ToBase64String(File.ReadAllBytes(settingsPath)):null;
        var old=Document("20000000000000000000000000000000",date,"Older private text\r\n  Žltý\t kôň");
        var newest=Document("30000000000000000000000000000000",date.AddMinutes(2),"Newest private text");
        var tied=Document("40000000000000000000000000000000",date.AddMinutes(2),"Same-time private text");
        var inserted=Document("50000000000000000000000000000000",date.AddMinutes(3),"Already inserted");inserted.InsertedAt=date.AddMinutes(4);
        var empty=Document("60000000000000000000000000000000",date.AddMinutes(5),"");
        foreach(var document in new[]{old,newest,tied,inserted,empty})owner.SaveReview(document);
        var foreign=Document("70000000000000000000000000000000",date.AddMinutes(6),"Foreign nonprivate memory entry",false);
        var privateMemory=Get<Dictionary<string,ReviewDocument>>(owner,"privateReviews");privateMemory.Add(foreign.Id,foreign);
        var initial=Choices(owner);var choices=Items(initial);
        Require(choices.Select(Id).SequenceEqual(new[]{newest.Id,tied.Id,old.Id})&&owner.PrivateReviewCount==3,"Choices and count must include only pending nonempty private text, newest first with deterministic ties.");
        Require(Preview(choices[2])=="Older private text Žltý kôň","Private preview must collapse line breaks and whitespace without losing Unicode text.");
        Require((DateTimeOffset)choices[2].GetType().GetProperty("Created")!.GetValue(choices[2])! ==date,"Private choice lost its recording date.");
        if(initial is IList writable)Require(writable.IsReadOnly,"Private choices expose a mutable list.");
        checks.Add("Private choices filter pending membership, preserve dates and Unicode text, and sort newest first with stable ties");

        var calls=new List<string>();var picker=Picker(calls.Add);Refresh(picker,owner);
        picker.Measure(new(360,400));picker.Arrange(new Rect(0,0,360,picker.DesiredSize.Height));picker.UpdateLayout();
        var select=Find<ComboBox>(picker).Single();var open=Find<Button>(picker).Single(b=>AutomationProperties.GetName(b)=="Open private transcript");
        Require(AutomationProperties.GetName(select)=="Private transcripts"&&select.Visibility==Visibility.Visible&&picker.Visibility==Visibility.Visible,"Many private transcripts lack an accessible visible selector.");
        select.SelectedValue=old.Id;Require(calls.Count==0&&owner.BannerReview==null,"Selecting a private transcript opened Review.");
        open.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Require(calls.SequenceEqual(new[]{old.Id}),"Explicit Open did not use the selected older transcript ID.");
        var itemsSource=select.ItemsSource;var selectedItem=select.SelectedItem;
        Refresh(picker,owner);Require(ReferenceEquals(select.ItemsSource,itemsSource)&&ReferenceEquals(select.SelectedItem,selectedItem),"Unrelated refresh replaces selector items and disturbs the current selection/focus.");
        Require((string)select.SelectedValue==old.Id&&calls.Count==1,"Refresh changed private selection or opened Review.");
        old.ManualFinalDraft="Edited older private words";owner.SaveReview(old);Refresh(picker,owner);
        Require((string)select.SelectedValue==old.Id&&Preview(select.SelectedItem!)=="Edited older private words"&&Preview(Items(initial)[2])=="Older private text Žltý kôň","Editing lost selection, failed to refresh preview or mutated an earlier choice snapshot.");
        Refresh(picker,owner,false);Require(!open.IsEnabled,"Busy/apply state leaves private opening enabled.");
        open.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Require(calls.Count==1,"A disabled Open action dispatched a private transcript.");
        Refresh(picker,owner,true);owner.DismissReview(old.Id);Refresh(picker,owner);
        Require((string)select.SelectedValue==newest.Id&&calls.Count==1,"Removing the selection failed to fall back to the newest remaining transcript without opening Review.");
        owner.DismissReview(newest.Id);Refresh(picker,owner);Require(select.Visibility==Visibility.Collapsed&&picker.Visibility==Visibility.Visible&&open.IsEnabled,"One private transcript needs a direct Open action without a visible selector.");
        open.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Require(calls.SequenceEqual(new[]{old.Id,tied.Id}),"The one-item Open action lost its transcript ID.");
        owner.DismissReview(tied.Id);Refresh(picker,owner);Require(picker.Visibility==Visibility.Collapsed&&!open.IsEnabled,"An empty pending private set retains a selectable/openable transcript.");
        Require(PresentationSource.FromVisual(picker)==null,"Private selector checks attached a native visual.");
        checks.Add("Private selector opens only by explicit action; older selection, unchanged refresh, edits, removal fallback, one/none and busy states");

        var marker=Document("80000000000000000000000000000000",date,"Existing banner private text");Set(owner,"bannerProgress",new CompactReviewProgress(marker));
        foreach(var id in new[]{old.Id,inserted.Id,empty.Id,foreign.Id,"not-a-current-private-id"})Open(owner,id);
        Require(ReferenceEquals(owner.BannerReview,marker),"Stale, inserted, empty or nonprivate ID replaced the current Review.");
        owner.SaveReview(newest);Set(owner,"active",BusyRun());Open(owner,newest.Id);Set(owner,"active",null);
        Set(owner,"applyingBannerId",marker.Id);Open(owner,newest.Id);Set(owner,"applyingBannerId",null);
        Set(owner,"reviewWindow",ApplyingDetails());Require(!owner.CanOpenPrivateReviews,"Detailed Review insertion leaves private opening available.");Open(owner,newest.Id);Set(owner,"reviewWindow",null);
        Set(owner,"exiting",true);Open(owner,newest.Id);Set(owner,"exiting",false);
        Require(ReferenceEquals(owner.BannerReview,marker),"Private opening bypassed a busy, applying or exiting operation.");Set(owner,"bannerProgress",null);
        Require(JsonSerializer.Serialize(owner.History)==originalHistory&&JsonSerializer.Serialize(owner.Pending)==originalPending,"Private flows changed protected History or Pending text.");SameFiles(originalFiles,fixture);
        checks.Add("Private ID opening rejects stale/nonprivate/inserted/empty IDs and busy/applying/exiting operations without native Review; protected History/Pending unchanged");

        // Force a truncation boundary inside a joined emoji and combining sequence.
        var unicode=Document("90000000000000000000000000000000",date.AddMinutes(7),string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦e\u0301 ",35)));owner.SaveReview(unicode);
        var preview=Preview(Items(Choices(owner)).Single(c=>Id(c)==unicode.Id));
        Require(!preview.Contains('\r')&&!preview.Contains('\n')&&!preview.Contains('\t')&&preview.EndsWith('…')&&StringInfo.ParseCombiningCharacters(preview).Length<=81,"Long private previews are not bounded single-line Unicode text.");
        Require(!preview.EndsWith("\u200d…",StringComparison.Ordinal)&&!preview.EndsWith("e…",StringComparison.Ordinal),"Private preview split a joined emoji or combining sequence.");
        Require(new System.Text.UTF8Encoding(false,true).GetString(new System.Text.UTF8Encoding(false,true).GetBytes(preview))==preview,"Private preview split a surrogate pair.");
        checks.Add("Long private previews truncate at Unicode text-element boundaries");
        var originalTheme=ThemeManager.Choice;
        try
        {
            foreach(var theme in new[]{"Light","Dark"})foreach(var width in new[]{640,1040})
            {
                ThemeManager.Apply(theme);
                foreach(var state in new[]{"none","one","many","busy","applying","details-applying"})
                {
                    privateMemory.Clear();if(state!="none")privateMemory.Add(newest.Id,newest);
                    if(state is "many" or "busy" or "applying" or "details-applying"){privateMemory.Add(old.Id,old);privateMemory.Add(tied.Id,tied);}
                    if(state=="busy")Set(owner,"active",BusyRun());
                    if(state=="applying")Set(owner,"applyingBannerId",newest.Id);
                    if(state=="details-applying")Set(owner,"reviewWindow",ApplyingDetails());
                    var window=Window(owner);
                    try
                    {
                        var actual=Get<FrameworkElement>(window,"privatePicker");var root=(MainWindowShell)window.Content;
                        if(state is "many" or "busy" or "applying" or "details-applying")
                        {
                            Find<ComboBox>(actual).Single().SelectedValue=old.Id;
                            typeof(SettingsWindow).GetMethod("RefreshPrivate",Fields)!.Invoke(window,null);
                            Require((string)Find<ComboBox>(actual).Single().SelectedValue==old.Id,"Actual Settings refresh lost the selected older private transcript.");
                        }
                        root.Measure(new(width,820));root.Arrange(new Rect(0,0,width,820));root.UpdateLayout();
                        SettingsHelpChecks.SettleForRender(root);
                        var page=(ScrollViewer)root.PageHost.Content;
                        var privacy=Get<FrameworkElement>(window,"privateNext");
                        var privacyCard=Ancestors(privacy).OfType<Border>().First(b=>b.Padding.Left==24);
                        page.ScrollToVerticalOffset(Math.Max(0,privacyCard.TranslatePoint(new Point(),(UIElement)page.Content).Y-16));root.UpdateLayout();SettingsHelpChecks.SettleForRender(root);
                        Require(new WindowInteropHelper(window).Handle==0&&PresentationSource.FromVisual(root)==null,"Actual Settings rendering opened a native window.");
                        Require(actual.Visibility==(state=="none"?Visibility.Collapsed:Visibility.Visible),"Actual Settings did not refresh the private selector.");
                        if(state!="none")
                        {
                            var actualOpen=Find<Button>(actual).Single(b=>AutomationProperties.GetName(b)=="Open private transcript");
                            Require(actualOpen.IsEnabled==(state is not ("busy" or "applying" or "details-applying")),"Actual Settings failed to disable private opening while busy.");
                            var contentEdge=actual.TranslatePoint(new Point(actual.ActualWidth,0),privacyCard).X;
                            Require(contentEdge<=privacyCard.ActualWidth-24-39,"Private selector escaped the aligned rightmost settings help gutter.");
                            Require(actual.ActualWidth>200,"Private selector lost usable width to an extra help gutter.");
                        }
                        var bitmap=new RenderTargetBitmap(width,820,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
                        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,$"private-transcripts-{state}-{width}-{theme}.png"));png.Save(file);
                    }
                    finally{window.Close();Set(owner,"active",null);Set(owner,"applyingBannerId",null);Set(owner,"reviewWindow",null);}
                    SameFiles(originalFiles,fixture);
                }
                checks.Add($"Actual Settings private none/one/many/busy/applying/details-applying layout at {width} DIP {theme}; no HWND, microphone enumeration or native input");
            }
        }
        finally{ThemeManager.Apply(originalTheme);}
        Require(JsonSerializer.Serialize(owner.History)==originalHistory&&JsonSerializer.Serialize(owner.Pending)==originalPending,"Settings private selection/rendering changed protected History/Pending.");SameFiles(originalFiles,fixture);
        Require((File.Exists(settingsPath)?Convert.ToBase64String(File.ReadAllBytes(settingsPath)):null)==originalSettings,"Private selection or refresh wrote Settings.");
    }
    static IEnumerable<DependencyObject> Ancestors(DependencyObject value)
    {for(var parent=VisualTreeHelper.GetParent(value);parent!=null;parent=VisualTreeHelper.GetParent(parent))yield return parent;}
}