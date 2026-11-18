using System.Windows.Controls;
using System.Windows.Threading;

namespace LocalDictation.App.Views;

public sealed partial class HistoryPanel
{
    readonly Button insertAtCursor;
    bool historyInsertRequested;
    string? SnapshotHistoryInsertionText()=>Selected==null?null:transcript.SelectionLength>0?transcript.SelectedText:transcript.Text;
    void UpdateHistoryInsertAction()=>insertAtCursor.IsEnabled=Selected!=null&&!historyInsertRequested&&controller.CanInsertHistoryAtCursor&&
        !string.IsNullOrWhiteSpace(SnapshotHistoryInsertionText());
    async void InsertAtCursor()
    {
        if(historyInsertRequested||Selected is not {} selected)return;
        // Freeze the exact highlight before focus or controller refresh changes it.
        var sourceId=selected.Id;var text=SnapshotHistoryInsertionText();
        if(string.IsNullOrWhiteSpace(text)){message.Text="Select transcript text to insert.";return;}
        historyInsertRequested=true;UpdateHistoryInsertAction();moreMenu.IsOpen=false;
        try
        {
            message.Text="Inserting…";
            // Reserve the direct command before deferring destination activation.
            var result=await controller.InsertHistoryAtCursorAfterMenuClosed(sourceId,text,async()=>
            {
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
                if(disposed)throw new OperationCanceledException();
            });
            if(!disposed)message.Text=result.Message;
        }
        catch(OperationCanceledException){if(!disposed)message.Text="Insertion cancelled. Your saved transcript is unchanged.";}
        finally{historyInsertRequested=false;if(!disposed)UpdateHistoryInsertAction();}
    }
}
