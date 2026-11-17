using System.Windows;
using System.Windows.Automation;
using LocalDictation.App.Services;
using LocalDictation.Core;

namespace LocalDictation.DeliveryProbe;

sealed partial class ProbeWindow
{
    async Task ExactFindTextContinuation()
    {
        const string prefix="Prefix | ",suffix=" | Suffix";
        var field=new CollapsedSelectionTextBox{Text=prefix+suffix,RefuseBackwardCharacterMovement=true,Margin=new(16)};
        var fixture=new Window{Title="Delivery probe — exact suffix lookup",Width=780,Height=200,
            Owner=this,Content=field,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        temporaryTarget=fixture;
        using var worker=new AutomationWorker();
        try
        {
            fixture.Show();fixture.Activate();field.Focus();field.Select(prefix.Length,0);await Settle();
            var owned=await CaptureOwn(worker,fixture);current=owned;
            async Task Apply(string text,bool final=false)
            {
                Require(await worker.Run(token=>owned.Apply(text,token,restoreFocus:final)),"Exact range update failed: "+owned.Reason);
                Require(field.Text==prefix+text+suffix,"Owned replacement changed surrounding text or duplicated a draft.");
                Require(owned.LastDiagnostic is {ExactText:true,ExactCaret:true},"The complete replacement and caret were not confirmed.");
            }
            await Apply("Prvá veta.");
            await Task.Delay(500);
            await Apply("Prvá veta pokračuje po pauze.");
            Require(owned.LastDiagnostic?.OwnedRangeStrategy=="exact-text"&&field.RefusedRangeMovements>0,"The fixture did not exercise unreliable character movement.");
            await Apply("Prvá veta pokračuje po dlhšej pauze.");
            await Apply("Celý opravený diktát.",final:true);
            Require(!ClipboardRecovery.HasPending&&!ClipboardRecovery.HasUnsettledTransaction,"The exact suffix fixture retained a clipboard lease.");
        }
        finally
        {
            fixture.Close();temporaryTarget=null;await Settle();
            if(ClipboardRecovery.HasPending)
                Require(ClipboardRecovery.RestoreAfterInspection().Succeeded,"Synchronous fixture recovery failed.");
        }
    }

    async Task EmptyPlaceholderContinuation(string placeholder="Write a message\n")
    {
        var field=new CollapsedSelectionTextBox{Text="",EmptyDocumentProjection=placeholder,Margin=new(16)};
        var fixture=new Window{Title="Delivery probe — independent empty value",Width=780,Height=200,
            Owner=this,Content=field,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        temporaryTarget=fixture;
        using var worker=new AutomationWorker();
        try
        {
            fixture.Show();fixture.Activate();field.Focus();field.Select(0,0);await Settle();
            var owned=await CaptureOwn(worker,fixture);current=owned;
            var guard=Field<TargetGuard>(owned,"guard");
            Require(guard.ExpectedText==""&&guard.Start==0,
                "The empty control value was mistaken for generated placeholder text.");
            async Task Apply(string text,bool final=false)
            {
                Require(await worker.Run(token=>owned.Apply(text,token,restoreFocus:final)),"Placeholder update failed: "+owned.Reason);
                Require(field.Text==text&&owned.InsertedText==text&&!owned.IsConflict,"The latest complete draft was not owned exactly.");
                Require(owned.LastDiagnostic is {ExactText:true,ExactCaret:true}&&owned.LastDiagnostic.Final==final,"Exact text, caret or final path was not confirmed.");
                Require(!ClipboardRecovery.HasPending&&!ClipboardRecovery.HasUnsettledTransaction,"A placeholder update retained a clipboard lease.");
            }
            await Apply("Prvá veta.");
            await Task.Delay(500);
            await Apply("Prvá veta. Pokračujem po pauze.");
            await Apply("Opravená veta. Pokračujem po pauze.");
            await Apply("");
            Require(guard.ExpectedText=="","Withdrawal adopted placeholder words as content.");
            await Apply("Celý konečný diktát.",final:true);
            // Real content with the same wording must not be discarded as a hint.
            field.Text="Write a message";field.Select(0,0);await Settle();
            owned=await CaptureOwn(worker,fixture);current=owned;
            guard=Field<TargetGuard>(owned,"guard");
            Require(guard.ExpectedText=="Write a message","Real text equal to a placeholder was discarded.");
            Require(await worker.Run(token=>owned.Apply("Diktát ",token)),"Insertion before real matching text failed.");
            Require(field.Text=="Diktát Write a message","Insertion discarded real existing text.");
            field.Text=" ";field.Select(0,0);await Settle();
            owned=await CaptureOwn(worker,fixture);current=owned;
            Require(Field<TargetGuard>(owned,"guard").ExpectedText==" ","Real whitespace was discarded.");
            field.Text="";field.EmptyDocumentProjection=placeholder;field.Select(0,0);field.HideValuePattern=true;await Settle();
            owned=await CaptureOwn(worker,fixture);current=owned;
            Require(Field<TargetGuard>(owned,"guard").ExpectedText==placeholder,"Absent ValuePattern was accepted as empty proof.");
            field.HideValuePattern=false;field.ValueReportsReadOnly=true;await Settle();
            owned=await CaptureOwn(worker,fixture);current=owned;
            Require(Field<TargetGuard>(owned,"guard").ExpectedText==placeholder,"Read-only ValuePattern was accepted as empty proof.");
            field.ValueReportsReadOnly=false;field.Text="";field.Select(0,0);await Settle();
            owned=await CaptureOwn(worker,fixture);current=owned;
            field.Text="Foreign changed text";field.ForceEmptyValue=true;field.Select(0,0);await Settle();
            Require(!await worker.Run(token=>owned.Apply("MUST NOT INSERT",token))&&owned.IsConflict,
                "A stale empty value hid foreign changed text.");
            Require(field.Text=="Foreign changed text"&&owned.LastDiagnostic?.QueuedInputEvents==0,
                "A contradictory field snapshot queued input or changed foreign text.");
            field.ForceEmptyValue=false;field.Text="";field.EmptyDocumentProjection=placeholder;field.Select(0,0);await Settle();
            owned=await CaptureOwn(worker,fixture);current=owned;
            Require(await worker.Run(token=>owned.Apply("Retained owned draft",token)),"Could not establish the withdrawal fixture.");
            field.RetainTextOnDelete=true;
            Require(!await worker.Run(token=>owned.Apply("",token))&&owned.IsConflict,
                "A stale empty value falsely acknowledged withdrawal of retained text.");
            Require(field.Text=="Retained owned draft"&&owned.InsertedText=="Retained owned draft",
                "Failed withdrawal lost the acknowledged owned draft.");
        }
        finally
        {
            fixture.Close();temporaryTarget=null;await Settle();
            if(ClipboardRecovery.HasPending)
                Require(ClipboardRecovery.RestoreAfterInspection().Succeeded,"Synchronous fixture recovery failed.");
            Require(!ClipboardRecovery.HasUnsettledTransaction,"The fixture must retain an unsettled clipboard backup.");
        }
    }
}
