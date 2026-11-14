using System.Reflection;
using System.Runtime.CompilerServices;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.Core;

static class HoldShortcutFlowChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static object Run()
    {
        var type=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;
        var run=RuntimeHelpers.GetUninitializedObject(type);
        Set(run,"Settings",new Settings{AfterRecording=AfterRecordingAction.PreviewFirst});
        Set(run,"CompletionGate",new CompletionDestinationGate());Set(run,"Recording",true);
        Set(run,"Capture",RuntimeHelpers.GetUninitializedObject(typeof(MicrophoneCapture)));
        return run;
    }
    public static void Check(string output,List<string> checks)
    {
        var stop=typeof(AppController).GetMethod("StopHeldShortcut",Fields);
        Require(stop!=null,"Hold release is not connected to the recording controller.");
        var owner=ReviewWorkflowChecks.Controller(output,new ReviewDocument());
        var owned=Run();Set(owner,"active",owned);Set(owner,"heldShortcutRun",owned);
        stop!.Invoke(owner,null);Require(!Get<bool>(owned,"Recording"),"Hold release did not Finish the recording it started.");
        var unrelated=Run();Set(owner,"active",unrelated);Set(owner,"heldShortcutRun",owned);
        stop.Invoke(owner,null);Require(Get<bool>(unrelated,"Recording"),"A stale hold release stopped an unrelated recording.");
        Require(Equals(typeof(AppController).GetMethod("SetShortcutCapture")!.Invoke(owner,[true]),false),"Shortcut editing was allowed to consume a live recording's release keys.");
        Set(owner,"active",null);
        var input=Get<GlobalInput>(owner,"input");var commands=Get<InputCommands>(input,"commands");
        owner.Settings.Shortcut="Ctrl+Alt+Space";owner.Settings.ShortcutMode=ShortcutMode.Hold;
        owner.RecheckShortcut();commands.Update(0x11,true,false,true);commands.Update(0x12,true,false,true);
        Require(commands.Update(0x20,true,false,true).Command==InputCommand.Start,"Hold mode was not applied to effective input configuration.");
        var changed=new Settings{Language="en",PrimaryModel="first",Shortcut="Ctrl+Shift+D",ShortcutMode=ShortcutMode.Toggle};
        owner.SaveSettings(changed);Require(input.Shortcut.ToString()=="Ctrl+Alt+Space"&&commands.IsHoldingShortcut,"Settings reset a live hold before its Stop could be delivered.");
        Require(commands.Update(0x20,false,false,true).Command==InputCommand.Stop,"A setting change swallowed the hold release.");
        stop.Invoke(owner,null);Require(input.Shortcut.ToString()=="Ctrl+Shift+D"&&commands.Mode==ShortcutMode.Toggle,"Deferred shortcut settings were not applied after release.");
        checks.Add("hold release owns its recording, ignores unrelated runs, blocks capture while busy and applies deferred settings after Stop");
        CheckCancellation(output,checks,stop);
    }

    static void CheckCancellation(string output,List<string> checks,MethodInfo stop)
    {
        foreach(var primaryFirst in new[]{false,true})
        {
            var owner=ReviewWorkflowChecks.Controller(output,new ReviewDocument());
            var input=Get<GlobalInput>(owner,"input");var commands=Get<InputCommands>(input,"commands");
            owner.Settings.Shortcut="Ctrl+Alt+Space";owner.Settings.ShortcutMode=ShortcutMode.Hold;owner.RecheckShortcut();
            var keys=primaryFirst?new[]{0x20,0x11,0x12}:new[]{0x11,0x12,0x20};
            foreach(var key in keys[..2])commands.Update(key,true,false,true);
            Require(commands.Update(keys[2],true,false,true).Command==InputCommand.Start,"Cancellation fixture did not start a hold.");
            var run=Run();using var cancel=new CancellationTokenSource();Set(run,"Cancel",cancel);
            Set(owner,"active",run);Set(owner,"heldShortcutRun",run);
            var changed=new Settings{Language="en",PrimaryModel="first",EnableShortcut=primaryFirst,
                Shortcut="Ctrl+Shift+D",ShortcutMode=primaryFirst?ShortcutMode.Hold:ShortcutMode.Toggle};
            owner.SaveSettings(changed);
            Require(input.Shortcut.ToString()=="Ctrl+Alt+Space"&&input.ShortcutEnabled&&input.IsHoldingShortcut,"A pending change reset a live hold before cancellation.");
            Require(commands.Update(0x1B,true,true,true)==new InputDecision(InputCommand.Cancel,true)&&!input.IsHoldingShortcut,"Escape did not cancel and relinquish the held gesture.");
            owner.Cancel();
            Require(cancel.IsCancellationRequested&&Get<bool>(run,"AutoInsertBlocked"),"Escape left the owned run or automatic insertion active.");
            Require(input.Shortcut.ToString()=="Ctrl+Shift+D"&&input.Mode==changed.ShortcutMode&&input.ShortcutEnabled==changed.EnableShortcut,
                "Escape left deferred shortcut mode/key/enabled settings unapplied without a subsequent Stop.");
            Require(commands.Update(0x1B,true,true,input.ShortcutEnabled)==new InputDecision(InputCommand.None,true),"Reconfiguration allowed an Escape autorepeat to cancel again.");
            Require(commands.Update(0x1B,false,false,input.ShortcutEnabled)==new InputDecision(InputCommand.None,true),"Reconfiguration leaked the consumed Escape key-up.");
            Require(commands.Update(0x11,false,false,input.ShortcutEnabled).Command==InputCommand.None,"Cancelled hold emitted a second Stop.");
            Require(commands.Update(0x11,true,false,input.ShortcutEnabled).Command==InputCommand.None,"A cancelled partial gesture rearmed after changing configuration.");
            commands.Update(0x12,false,false,input.ShortcutEnabled);commands.Update(0x11,false,false,input.ShortcutEnabled);
            Require(commands.Update(0x20,false,false,input.ShortcutEnabled)==new InputDecision(InputCommand.None,!primaryFirst),"Cancellation unbalanced the original shortcut's primary key-up.");
            commands.Update(0x11,true,false,input.ShortcutEnabled);commands.Update(0x10,true,false,input.ShortcutEnabled);
            Require(commands.Update(0x44,true,false,input.ShortcutEnabled).Command==(primaryFirst?InputCommand.Start:InputCommand.None),"Next gesture ignored the effective post-cancellation configuration.");
            checks.Add("Escape applies deferred shortcut "+(primaryFirst?"mode/key":"disable/mode/key")+" changes and preserves gesture/key-up ownership");
        }

        var buttonOwner=ReviewWorkflowChecks.Controller(output,new ReviewDocument());
        var buttonInput=Get<GlobalInput>(buttonOwner,"input");var buttonCommands=Get<InputCommands>(buttonInput,"commands");
        buttonOwner.Settings.Shortcut="Ctrl+Space";buttonOwner.Settings.ShortcutMode=ShortcutMode.Hold;buttonOwner.RecheckShortcut();
        buttonCommands.Update(0x11,true,false,true);buttonCommands.Update(0x20,true,false,true);
        var buttonRun=Run();using var buttonCancel=new CancellationTokenSource();Set(buttonRun,"Cancel",buttonCancel);
        Set(buttonOwner,"active",buttonRun);Set(buttonOwner,"heldShortcutRun",buttonRun);
        buttonOwner.SaveSettings(new Settings{Language="en",PrimaryModel="first",EnableShortcut=false,Shortcut="Ctrl+Shift+D"});
        buttonOwner.Cancel();
        Require(buttonCancel.IsCancellationRequested&&buttonInput.IsHoldingShortcut&&buttonInput.Shortcut.ToString()=="Ctrl+Space"&&buttonInput.ShortcutEnabled,
            "Button cancellation reset a physically active hold before its release.");
        Require(buttonCommands.Update(0x20,false,true,true)==new InputDecision(InputCommand.Stop,true),"Button cancellation swallowed the held shortcut's Stop or key-up.");
        stop.Invoke(buttonOwner,null);
        Require(!buttonInput.ShortcutEnabled&&buttonInput.Shortcut.ToString()=="Ctrl+Shift+D","Button-cancelled hold did not apply settings on its eventual release.");
        checks.Add("button cancellation keeps an active hold until release before applying deferred settings");
    }
}
