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
    public int CollapsedProviderSelections {get;private set;}
    public int SuppressedPastes {get;private set;}
    public bool ProviderPoisoned=>poisoned;

    protected override AutomationPeer OnCreateAutomationPeer()=>new Peer(this);
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if(poisoned&&e.Key==Key.V&&(Keyboard.Modifiers&ModifierKeys.Control)!=0)
        {SuppressedPastes++;e.Handled=true;return;}
        base.OnPreviewKeyDown(e);
    }
    void SelectedCollapsed(){CollapsedProviderSelections++;poisoned=true;}

    sealed class Peer(CollapsedSelectionTextBox owner) : TextBoxAutomationPeer(owner)
    {
        public override object? GetPattern(PatternInterface patternInterface)
        {
            var pattern=base.GetPattern(patternInterface);
            return patternInterface==PatternInterface.Text&&pattern is ITextProvider text
                ?new Provider(owner,text):pattern;
        }
    }
    sealed class Provider(CollapsedSelectionTextBox owner,ITextProvider inner) : ITextProvider
    {
        ITextRangeProvider Wrap(ITextRangeProvider range)=>new Range(owner,range);
        public ITextRangeProvider DocumentRange=>Wrap(inner.DocumentRange);
        public SupportedTextSelection SupportedTextSelection=>inner.SupportedTextSelection;
        public ITextRangeProvider[] GetSelection()=>owner.poisoned?[]:inner.GetSelection().Select(Wrap).ToArray();
        public ITextRangeProvider[] GetVisibleRanges()=>inner.GetVisibleRanges().Select(Wrap).ToArray();
        public ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement)=>Wrap(inner.RangeFromChild(childElement));
        public ITextRangeProvider RangeFromPoint(Point screenLocation)=>Wrap(inner.RangeFromPoint(screenLocation));
    }
    sealed class Range(CollapsedSelectionTextBox owner,ITextRangeProvider inner) : ITextRangeProvider
    {
        readonly ITextRangeProvider source=inner;
        static ITextRangeProvider Unwrap(ITextRangeProvider range)=>range is Range wrapper?wrapper.source:range;
        ITextRangeProvider? Wrap(ITextRangeProvider? range)=>range==null?null:new Range(owner,range);
        public ITextRangeProvider Clone()=>Wrap(source.Clone())!;
        public bool Compare(ITextRangeProvider range)=>source.Compare(Unwrap(range));
        public int CompareEndpoints(TextPatternRangeEndpoint endpoint,ITextRangeProvider targetRange,TextPatternRangeEndpoint targetEndpoint)
            =>source.CompareEndpoints(endpoint,Unwrap(targetRange),targetEndpoint);
        public void ExpandToEnclosingUnit(TextUnit unit)=>source.ExpandToEnclosingUnit(unit);
        public ITextRangeProvider? FindAttribute(int attributeId,object value,bool backward)=>Wrap(source.FindAttribute(attributeId,value,backward));
        public ITextRangeProvider? FindText(string text,bool backward,bool ignoreCase)=>Wrap(source.FindText(text,backward,ignoreCase));
        public object GetAttributeValue(int attributeId)=>source.GetAttributeValue(attributeId);
        public double[] GetBoundingRectangles()=>source.GetBoundingRectangles();
        public IRawElementProviderSimple[] GetChildren()=>source.GetChildren();
        public IRawElementProviderSimple GetEnclosingElement()=>source.GetEnclosingElement();
        public string GetText(int maxLength)=>source.GetText(maxLength);
        public int Move(TextUnit unit,int count)=>source.Move(unit,count);
        public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint,ITextRangeProvider targetRange,TextPatternRangeEndpoint targetEndpoint)
            =>source.MoveEndpointByRange(endpoint,Unwrap(targetRange),targetEndpoint);
        public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint,TextUnit unit,int count)=>source.MoveEndpointByUnit(endpoint,unit,count);
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
