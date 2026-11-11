using System.ComponentModel;

namespace LocalDictation.Core;

/// <summary>A failed disk write preserves the in-memory Review and its unsaved status.</summary>
public sealed class PendingReviews
{
    readonly ReviewStore store;
    readonly List<ReviewDocument> documents;
    readonly HashSet<string> unsaved=[];
    public IReadOnlyList<ReviewDocument> Documents => documents;
    public IReadOnlySet<string> UnsavedIds => unsaved;
    public PendingReviews(ReviewStore store)
    {
        this.store=store;
        documents=store.Load().ToList();
    }
    public bool Save(ReviewDocument document)
    {
        var index=documents.FindIndex(d=>d.Id==document.Id);
        if(index<0)documents.Insert(0,document);
        else documents[index]=document;
        try
        {
            store.Save(document);
            unsaved.Remove(document.Id);
            return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            unsaved.Add(document.Id);
            return false;
        }
    }
    public void Dismiss(string id)
    {
        // If deletion fails, leave the pending item and its state untouched.
        store.Delete(id);
        documents.RemoveAll(d=>d.Id==id);
        unsaved.Remove(id);
    }
}
