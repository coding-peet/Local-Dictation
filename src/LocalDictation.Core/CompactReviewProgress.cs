namespace LocalDictation.Core;

public sealed class CompactReviewProgress
{
    public ReviewDocument Document {get;}
    public string? SelectedId {get;private set;}
    public bool IsComplete=>Document.UnresolvedCount==0;

    public CompactReviewProgress(ReviewDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document=document;Refresh();
    }

    public void Select(string id)
    {
        if(!Document.Pieces.Any(p=>p.Different&&p.Id==id))
            throw new ArgumentException("Unknown difference.",nameof(id));
        SelectedId=id;
    }

    public void Choose(string kind,string? custom=null)
    {
        var index=Document.Pieces.FindIndex(p=>p.Different&&p.Id==SelectedId);
        if(index<0)throw new InvalidOperationException("Select a difference before choosing wording.");
        Document.Choose(Document.Pieces[index].Id,kind,custom);
        SelectedId=null;
        for(var offset=1;offset<=Document.Pieces.Count;offset++)
        {
            var next=Document.Pieces[(index+offset)%Document.Pieces.Count];
            if(!next.Different||Document.Choices.ContainsKey(next.Id))continue;
            SelectedId=next.Id;break;
        }
    }

    public void KeepRemaining()
    {
        Document.KeepRemaining();SelectedId=null;
    }

    public void Refresh()
    {
        if(SelectedId!=null&&Document.Pieces.Any(p=>p.Different&&p.Id==SelectedId))return;
        SelectedId=Document.Pieces.FirstOrDefault(p=>p.Different&&!Document.Choices.ContainsKey(p.Id))?.Id;
    }
}
