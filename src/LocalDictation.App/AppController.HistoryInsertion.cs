using LocalDictation.App.Services;
using LocalDictation.Core;

namespace LocalDictation.App;

public sealed partial class AppController
{
    LastExternalWindowTracker? historyWindowTracker;
    readonly Func<HistoryInsertionDestination?> historyInsertionDestination=()=>null;
    readonly Action<HistoryInsertionDestination,CancellationToken> restoreHistoryDestination=WindowsTextDelivery.ActivateHistoryDestination;
    Dictionary<string,ReviewDocument>? historyInsertCopies;
    sealed record HistoryCopyAttempt(string SourceId,string Text,HistoryInsertionDestination Destination,ReviewDocument Document);
    HistoryCopyAttempt? activeHistoryInsertion,historyInsertionRetry;
    CancellationTokenSource? historyInsertionCancellation;
    bool historyInsertionBusy;
    DateTimeOffset historyInsertionExpiresAt;
    bool HistoryInsertionWorkRunning=>historyInsertionRetry!=null&&automation?.UnfinishedWork!=null;
    bool HistoryAttemptHasOutstandingWork(HistoryCopyAttempt attempt)=>automation?.UnfinishedWork!=null||
        destinations.TryGetValue(attempt.Document.Id,out var target)&&target is WindowsTextDelivery native&&
        (native.HasPendingAcknowledgement||native.ConfirmedPendingPaste!=null);
    void PruneHistoryInsertionAttempt()
    {
        if(historyInsertionBusy||deliveryBusy||historyInsertionRetry is not {} attempt||
            HistoryAttemptHasOutstandingWork(attempt)||DateTimeOffset.UtcNow<historyInsertionExpiresAt)return;
        historyInsertionRetry=null;if(IsHistoryInsertCopy(attempt.Document.Id))DismissReview(attempt.Document.Id);
    }
    async Task NotifyHistoryWorkCompletion(Task work)
    {
        try{await work.ConfigureAwait(false);}catch(Exception){}
        var dispatcher=System.Windows.Application.Current?.Dispatcher;
        if(disposed||dispatcher==null||dispatcher.HasShutdownStarted)return;
        _=dispatcher.BeginInvoke(()=>{if(disposed)return;DrainConfirmedPastes();Changed?.Invoke();});
    }
    bool IsHistoryInsertCopy(string id)=>historyInsertCopies?.ContainsKey(id)==true;
    public bool CanInsertHistoryAtCursor=>!exiting&&!IsBusy&&!IsApplyingReview&&inserting.Count==0&&!deliveryBusy;
    bool HistoryCopyCanDispatch(ReviewDocument document)=>!IsHistoryInsertCopy(document.Id)||
        activeHistoryInsertion is {} attempt&&attempt.Document.Id==document.Id&&historyInsertionCancellation?.IsCancellationRequested==false&&
        insertionForegroundWindow()==attempt.Destination.Window;

    public Task<ReviewInsertionResult> InsertHistoryAtCursor(string sourceId,string text)=>
        InsertHistoryAtCursorAfterMenuClosed(sourceId,text,()=>Task.CompletedTask);
    public async Task<ReviewInsertionResult> InsertHistoryAtCursorAfterMenuClosed(string sourceId,string text,Func<Task> menuClosed)
    {
        if(!CanInsertHistoryAtCursor)return new(false,"Finish the current operation before inserting from History.");
        DrainConfirmedPastes();
        if(ClipboardRecovery.HasPending||ClipboardRecovery.HasUnsettledTransaction)
            return new(false,"Windows is still returning the previous clipboard. Wait before inserting again.");
        if(string.IsNullOrWhiteSpace(text))return new(false,"Select transcript text to insert.");
        if(historyWindowTracker is {IsAvailable:false})return new(false,"Windows destination tracking is unavailable. Use Copy text or restart Local Dictation.");
        var destination=historyInsertionDestination();
        if(destination==null)return new(false,"Click a text field in another application, return to History and try Insert again.");
        var attempt=historyInsertionRetry;
        if(attempt!=null&&(attempt.SourceId!=sourceId||attempt.Text!=text||attempt.Destination!=destination))
        {
            if(HistoryAttemptHasOutstandingWork(attempt))return new(false,"The previous insertion is still being confirmed. Wait before inserting other text.");
            if(IsHistoryInsertCopy(attempt.Document.Id))DismissReview(attempt.Document.Id);
            historyInsertionRetry=null;attempt=null;
        }
        if(attempt?.Document.InsertedAt!=null)
        {historyInsertionRetry=null;return new(true,"The earlier History insertion was confirmed. Nothing was inserted again.");}
        if(attempt!=null&&HistoryAttemptHasOutstandingWork(attempt))
            return new(false,"The previous insertion is still being confirmed. Wait before trying Insert again.");
        if(attempt==null)
        {
            var document=new ReviewDocument{PrimaryModel="history-copy",RequiresExplicitInsert=true};document.SetPrimary(text);
            attempt=new(sourceId,text,destination,document);
            historyInsertCopies??=[];historyInsertCopies[document.Id]=document;
        }
        var copy=attempt.Document;
        using var cancelled=new CancellationTokenSource();historyInsertionCancellation=cancelled;
        historyInsertionBusy=true;activeHistoryInsertion=attempt;
        try
        {
            Changed?.Invoke();
            await menuClosed();cancelled.Token.ThrowIfCancellationRequested();
            await automation.Run(token=>
            {
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,cancelled.Token);
                restoreHistoryDestination(destination,linked.Token);linked.Token.ThrowIfCancellationRequested();return true;
            });
            cancelled.Token.ThrowIfCancellationRequested();
            if(!HistoryCopyCanDispatch(copy))return new(false,"The destination changed. Your saved transcript is unchanged.");
            var result=await InsertReviewAtSelectedCursor(copy);
            if(result.Success)historyInsertionRetry=null;
            return result;
        }
        catch(OperationCanceledException){return new(false,"Insertion stopped because you changed the cursor or typed. Your saved transcript is unchanged.");}
        catch(InvalidOperationException error){return new(false,error.Message);}
        catch(Exception error){Diagnostics.Error("History cursor insertion",error);return new(false,"The previous destination could not be validated. Click its text field and try Insert again.");}
        finally
        {
            historyInsertionCancellation=null;activeHistoryInsertion=null;historyInsertionBusy=false;
            // A caller timeout or missing transport flag does not disprove a late paste.
            if(copy.InsertedAt==null&&(destinations.ContainsKey(copy.Id)||automation.UnfinishedWork!=null))
            {
                historyInsertionRetry=attempt;historyInsertionExpiresAt=DateTimeOffset.UtcNow.AddMinutes(2);
                if(automation.UnfinishedWork is {} unfinished)_=NotifyHistoryWorkCompletion(unfinished);
            }
            else if(copy.InsertedAt==null)DismissReview(copy.Id);
            Changed?.Invoke();
        }
    }
}
