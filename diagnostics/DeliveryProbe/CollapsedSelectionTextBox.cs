using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Text;
using System.Windows.Controls;
using System.Windows.Input;

namespace LocalDictation.DeliveryProbe;

// Synthetic provider behavior only. This control never queries another window.
// It models an editor whose caret is lost by selecting an already collapsed UIA
// range. Ordinary noncollapsed selections retain the native TextBox behavior.
sealed class CollapsedSelectionTextBox : TextBox
{
    bool poisoned;
    public string? EmptyDocumentProjection {get;set;}
    public bool ForceEmptyValue {get;set;}
    public bool HideValuePattern {get;set;}
    public bool ValueReportsReadOnly {get;set;}
    public bool RetainTextOnDelete {get;set;}
    public int CollapsedProviderSelections {get;private set;}
    public int SuppressedPastes {get;private set;}
    public int PasteCommands {get;private set;}
    public bool SuppressFindText {get;set;}
    public int SuppressedFindTextCalls {get;private set;}
    public bool RefuseBackwardCharacterMovement {get;set;}
    public int RefusedRangeMovements {get;private set;}
    public bool ThrowStaleRangeClone {get;set;}
    public int StaleRangeCloneFailures {get;private set;}
    public bool ProviderPoisoned=>poisoned;

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        // Model an initial-only accessibility projection, not a hint reappearing
        // after an acknowledged write has retired the production lease's projection.
        if(Text.Length>0)EmptyDocumentProjection=null;
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new Peer(this);
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.Delete&&RetainTextOnDelete)
        {e.Handled=true;ForceEmptyValue=true;Select(0,0);return;}
        if(e.Key==Key.V&&(Keyboard.Modifiers&ModifierKeys.Control)!=0)PasteCommands++;
        if(poisoned&&e.Key==Key.V&&(Keyboard.Modifiers&ModifierKeys.Control)!=0)
        {SuppressedPastes++;e.Handled=true;return;}
        base.OnPreviewKeyDown(e);
    }
    void SelectedCollapsed(){CollapsedProviderSelections++;poisoned=true;}

    sealed class Peer(CollapsedSelectionTextBox owner) : TextBoxAutomationPeer(owner)
    {
        public override object? GetPattern(PatternInterface patternInterface)
        {
            if(patternInterface==PatternInterface.Value&&owner.HideValuePattern)return null;
            var pattern=base.GetPattern(patternInterface);
            if(patternInterface==PatternInterface.Value&&pattern is IValueProvider value)
                return new ValueProvider(owner,value);
            return patternInterface==PatternInterface.Text&&pattern is ITextProvider text
                ?new Provider(owner,text):pattern;
        }
    }
    sealed class ValueProvider(CollapsedSelectionTextBox owner,IValueProvider inner) : IValueProvider
    {
        public bool IsReadOnly=>owner.ValueReportsReadOnly||inner.IsReadOnly;
        public string Value=>owner.ForceEmptyValue?"":inner.Value;
        public void SetValue(string value)=>inner.SetValue(value);
    }
    sealed class Provider(CollapsedSelectionTextBox owner,ITextProvider inner) : ITextProvider
    {
        ITextRangeProvider Wrap(ITextRangeProvider range,bool document=false)=>new Range(owner,range,owner.PasteCommands,document);
        public ITextRangeProvider DocumentRange=>Wrap(inner.DocumentRange,true);
        public SupportedTextSelection SupportedTextSelection=>inner.SupportedTextSelection;
        public ITextRangeProvider[] GetSelection()=>owner.poisoned?[]:inner.GetSelection().Select(range=>Wrap(range)).ToArray();
        public ITextRangeProvider[] GetVisibleRanges()=>inner.GetVisibleRanges().Select(range=>Wrap(range)).ToArray();
        public ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement)=>Wrap(inner.RangeFromChild(childElement));
        public ITextRangeProvider RangeFromPoint(Point screenLocation)=>Wrap(inner.RangeFromPoint(screenLocation));
    }
    sealed class Range(CollapsedSelectionTextBox owner,ITextRangeProvider inner,int creationPasteCount,bool document=false) : ITextRangeProvider
    {
        readonly ITextRangeProvider source=inner;
        bool fullDocument=document;
        static ITextRangeProvider Unwrap(ITextRangeProvider range)=>range is Range wrapper?wrapper.source:range;
        ITextRangeProvider? Wrap(ITextRangeProvider? range)=>range==null?null:new Range(owner,range,creationPasteCount,fullDocument);
        public ITextRangeProvider Clone()
        {
            if(owner.ThrowStaleRangeClone&&creationPasteCount==0&&owner.PasteCommands>0)
            {
                owner.StaleRangeCloneFailures++;
                throw new InvalidOperationException("Synthetic pre-paste range expired.");
            }
            return Wrap(source.Clone())!;
        }
        public bool Compare(ITextRangeProvider range)=>source.Compare(Unwrap(range));
        public int CompareEndpoints(TextPatternRangeEndpoint endpoint,ITextRangeProvider targetRange,TextPatternRangeEndpoint targetEndpoint)
            =>source.CompareEndpoints(endpoint,Unwrap(targetRange),targetEndpoint);
        public void ExpandToEnclosingUnit(TextUnit unit)=>source.ExpandToEnclosingUnit(unit);
        public ITextRangeProvider? FindAttribute(int attributeId,object value,bool backward)=>Wrap(source.FindAttribute(attributeId,value,backward));
        public ITextRangeProvider? FindText(string text,bool backward,bool ignoreCase)
        {
            if(owner.SuppressFindText){owner.SuppressedFindTextCalls++;return null;}
            return Wrap(source.FindText(text,backward,ignoreCase));
        }
        public object GetAttributeValue(int attributeId)=>source.GetAttributeValue(attributeId);
        public double[] GetBoundingRectangles()=>source.GetBoundingRectangles();
        public IRawElementProviderSimple[] GetChildren()=>source.GetChildren();
        public IRawElementProviderSimple GetEnclosingElement()=>source.GetEnclosingElement();
        public string GetText(int maxLength)
        {
            var text=source.GetText(maxLength);
            if(text.Length==0&&fullDocument&&owner.EmptyDocumentProjection is {} projection)
                return maxLength<0?projection:projection[..Math.Min(maxLength,projection.Length)];
            return text;
        }
        public int Move(TextUnit unit,int count)=>source.Move(unit,count);
        public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint,ITextRangeProvider targetRange,TextPatternRangeEndpoint targetEndpoint)
        {fullDocument=false;source.MoveEndpointByRange(endpoint,Unwrap(targetRange),targetEndpoint);}
        public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint,TextUnit unit,int count)
        {
            fullDocument=false;
            if(owner.RefuseBackwardCharacterMovement&&endpoint==TextPatternRangeEndpoint.Start&&unit==TextUnit.Character&&count<0)
            {owner.RefusedRangeMovements++;return count;}
            return source.MoveEndpointByUnit(endpoint,unit,count);
        }
        public void AddToSelection()=>source.AddToSelection();
        public void RemoveFromSelection()=>source.RemoveFromSelection();
        public void ScrollIntoView(bool alignToTop)=>source.ScrollIntoView(alignToTop);
        public void Select()
        {
            if(source.CompareEndpoints(TextPatternRangeEndpoint.Start,source,TextPatternRangeEndpoint.End)==0)owner.SelectedCollapsed();
            source.Select();
        }
    }
}
