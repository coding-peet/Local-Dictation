using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

internal static class Program
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly Type RunType=typeof(AppController).GetNestedType("Run",BindingFlags.NonPublic)!;

    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length!=0&&(args.Length!=2||args[0]!="--report"))
        {Console.Error.WriteLine("Usage: CompactFlowProbe [--report absolute-json-path]");return 2;}
        var report=args.Length==2?Path.GetFullPath(args[1]):null;
        var originalHome=Environment.GetEnvironmentVariable("LOCAL_DICTATION_HOME");
        var root=Path.Combine(Path.GetTempPath(),"LocalDictation-CompactFlowProbe-"+Guid.NewGuid().ToString("N"));
        var results=new List<CaseResult>();
        var started=DateTimeOffset.UtcNow;var exitCode=1;
        var app=new LocalDictation.App.App();
        app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var dispatcher=Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(new Action(async()=>
        {
            try
            {
                await Case("Preview updates stay out of the destination until completion",async test=>
                {
                    var doc=Text("Live draft");var fake=new FakeDelivery();
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false},primaryFinal:false,completed:false);
                    await test.Handle(run,"""{"type":"transcript.updated","text":"Live draft grows"}""");
                    Require(fake.ApplyCount==0,"Preview-only partial must not write.");
                    Require(doc.Primary=="Live draft grows","Partial transcript should remain visible/preserved.");
                    Require(test.Controller.BannerReview==null,"Review must not open before completion.");
                });

                await Case("Final events deliver ordinary dictation without exposing Review",async test=>
                {
                    var doc=Text("Draft preview");var fake=new FakeDelivery();
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false},primaryFinal:false,completed:false);
                    var exposedReview=false;
                    test.Controller.Changed+=()=>exposedReview|=test.Controller.BannerReview!=null;
                    await test.Handle(run,"""{"type":"transcript.final","text":"Finished at the original cursor"}""");
                    Require(fake.ApplyCount==0,"Preview-only final text must wait for successful session completion.");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(!exposedReview,"Ordinary successful completion exposed interactive Review before insertion.");
                    Require(doc.InsertedAt!=null&&fake.InsertedText=="Finished at the original cursor","The final event text was not delivered.");
                    Require(fake.ApplyCount==1&&fake.LastRestoreFocus,"Completion must return once to the retained original destination.");
                    Require(test.Controller.Pending.Count==0,"Delivered text must leave pending Review.");
                    test.NoLargeWindow();
                });

                await Case("Preview verification agreement does not withdraw an empty draft or expose Review",async test=>
                {
                    var doc=Text("Draft preview");doc.VerificationModel="verify/model";var fake=new FakeDelivery();
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false,VerifyAfterRecording=true},primaryFinal:false,completed:false);
                    var exposedReview=false;
                    test.Controller.Changed+=()=>exposedReview|=test.Controller.BannerReview!=null;
                    await test.Handle(run,"""{"type":"transcript.final","text":"Both results agree"}""");
                    Require(fake.ApplyCount==0,"Preview-only dictation has no owned live draft to withdraw.");
                    await test.Handle(run,"""{"type":"verification.started"}""");
                    await test.Handle(run,"""{"type":"verification.completed","verification":"Both results agree","pieces":[{"id":"agreement","primary":"Both results agree","verification":"Both results agree","different":false}]}""");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(!exposedReview,"Verification agreement exposed a needless interactive Review.");
                    Require(doc.InsertedAt!=null&&fake.ApplyCount==1&&fake.InsertedText=="Both results agree","Agreement did not return once to the original cursor.");
                    test.NoLargeWindow();
                });

                await Case("An older Review cannot publish or insert while a new destination is being captured",async test=>
                {
                    var older=Differences();test.Controller.OpenReview(older);
                    var run=test.Run(Text("New session"),new FakeDelivery(),new Settings{InsertAtCursor=false},primaryFinal:false,completed:false);
                    Set(run,"DestinationChecked",false);
                    var notifications=0;test.Controller.Changed+=()=>notifications++;
                    test.Controller.SelectBannerPiece("second");
                    test.Controller.ChooseBannerPiece("verification");
                    test.Controller.EditBannerPiece("Edited old wording");
                    test.Controller.KeepBannerWording();
                    await test.Controller.ApplyBannerReview();
                    test.Controller.OpenBannerDetails();
                    Require(notifications==0,"An old Review changed the UI during the initial destination check.");
                    Require(older.Choices.Count==0&&older.InsertedAt==null,"An older Review was changed during a new recording.");
                    Require(test.Controller.BannerReview==older,"The existing Review surface changed before initial capture finished.");
                    test.NoLargeWindow();
                });

                await Case("Completed preview inserts once and clears compact Review",async test=>
                {
                    var doc=Text("Completed preview");var fake=new FakeDelivery();
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false});
                    await test.Complete(run);
                    Require(fake.ApplyCount==1&&fake.InsertedText==doc.OutputText,"Completed preview must insert exactly once.");
                    Require(doc.InsertedAt!=null,"Successful placement must have a receipt.");
                    Require(test.Controller.BannerReview==null,"Successful placement must clear compact Review.");
                    Require(test.Controller.Pending.Count==0,"Inserted item must leave pending Review.");
                    Require(!test.Controller.IsBusy,"Completion must clear active recording before inserting.");
                    await test.Controller.ApplyBannerReview();
                    Require(fake.ApplyCount==1,"Repeated completion action must not insert twice.");
                    test.NoLargeWindow();
                });

                await Case("Finish keeps the captured destination through final delivery",async test=>
                {
                    var doc=Text("Draft preview");var fake=new FakeDelivery();
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false},primaryFinal:false,completed:false);
                    Set(run,"Recording",true);Set(run,"Closing",false);
                    test.Controller.Stop();
                    Require(ReferenceEquals(Field<ITextDelivery>(run,"Delivery"),fake),"Finish replaced the original captured destination.");
                    Require(!test.Controller.IsRecording,"Finish did not stop recording.");
                    await test.Handle(run,"""{"type":"transcript.final","text":"Final text after Finish"}""");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(doc.InsertedAt!=null&&fake.InsertedText=="Final text after Finish","Finish lost the original destination.");
                    Require(fake.ApplyCount==1&&fake.LastRestoreFocus,"Final delivery must use the retained destination with focus restoration.");
                });

                await Case("Completed text without a captured field waits for a destination decision",async test=>
                {
                    var doc=Text("Draft preview");var fake=new FakeDelivery();
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false},primaryFinal:false,completed:false);
                    Set(run,"Delivery",null);Field<Dictionary<string,ITextDelivery>>(test.Controller,"destinations").Remove(doc.Id);
                    await test.Handle(run,"""{"type":"transcript.final","text":"Recovered complete text"}""");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(doc.InsertedAt==null&&fake.ApplyCount==0,"Missing-target completion must not adopt a field or mark insertion.");
                    Require(test.Controller.BannerReview==doc&&doc.Reason.Contains("Choose destination",StringComparison.Ordinal),"Missing target needs an explicit destination decision with text preserved.");
                });

                await Case("Verified differences stay compact and the final choice inserts",async test=>
                {
                    var doc=Text("Draft preview");doc.VerificationModel="verify/model";var fake=new FakeDelivery();
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false,VerifyAfterRecording=true},primaryFinal:false,completed:false);
                    await test.Handle(run,"""{"type":"transcript.final","text":"The original text."}""");
                    await test.Handle(run,"""{"type":"verification.started"}""");
                    await test.Handle(run,"""{"type":"verification.completed","verification":"The revised wording.","pieces":[{"id":"prefix","primary":"The ","verification":"The ","different":false},{"id":"first","primary":"original","verification":"revised","different":true},{"id":"gap","primary":" ","verification":" ","different":false},{"id":"second","primary":"text","verification":"wording","different":true},{"id":"end","primary":".","verification":".","different":false}]}""");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(test.Controller.BannerReview==doc,"Differences must remain in the banner.");
                    Require(fake.ApplyCount==0,"Unresolved wording must not be inserted.");
                    Require(test.Controller.SelectedReviewPieceId=="first","First unresolved phrase should be active.");
                    test.Controller.ChooseBannerPiece("verification");
                    Require(test.Controller.SelectedReviewPieceId=="second","Choice must advance automatically.");
                    Require(fake.ApplyCount==0,"One remaining difference must prevent insertion.");
                    test.Controller.ChooseBannerPiece("primary");
                    await Until(()=>doc.InsertedAt!=null&&!test.Controller.IsApplyingReview);
                    Require(fake.ApplyCount==1,"Last choice must insert exactly once.");
                    Require(fake.InsertedText=="The revised text.","Chosen wording must be inserted exactly.");
                    Require(test.Controller.BannerReview==null,"Accepted banner must close its Review.");
                    Require(test.Controller.LatestText==doc.OutputText,"The completed banner must retain the approved wording.");
                    test.NoLargeWindow();
                });

                await Case("Agreement after completed verification inserts without a large window",async test=>
                {
                    var doc=Text("Both results agree");doc.VerificationModel="verify/model";
                    doc.Verification=doc.Primary;doc.VerificationCompleted=true;doc.DraftWithdrawn=true;
                    var fake=new FakeDelivery();
                    await test.Complete(test.Run(doc,fake,new Settings{InsertAtCursor=false,VerifyAfterRecording=true}));
                    Require(fake.ApplyCount==1&&doc.InsertedAt!=null,"Completed agreement should insert.");
                    Require(test.Controller.BannerReview==null,"Agreement must not leave a needless Review.");
                    test.NoLargeWindow();
                });

                await Case("Cancelled sessions preserve text without automatic insertion",async test=>
                {
                    var doc=Text("Cancelled draft");var fake=new FakeDelivery();
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false},primaryFinal:false,completed:false);
                    await test.Handle(run,"""{"type":"transcript.updated","text":"Cancelled draft"}""");
                    await test.Handle(run,"""{"type":"session.cancelled"}""");
                    await test.Complete(run);
                    Require(fake.ApplyCount==0,"Cancellation must block automatic placement.");
                    Require(doc.InsertedAt==null&&test.Controller.BannerReview==doc,"Cancelled draft must remain pending in banner.");
                    test.NoLargeWindow();
                });

                await Case("Failed requested verification blocks automatic insertion",async test=>
                {
                    var doc=Text("Unverified draft");doc.VerificationModel="verify/model";var fake=new FakeDelivery();
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false,VerifyAfterRecording=true},primaryFinal:false,completed:false);
                    await test.Handle(run,"""{"type":"transcript.final","text":"Unverified draft"}""");
                    await test.Handle(run,"""{"type":"verification.started"}""");
                    await test.Handle(run,"""{"type":"verification.failed"}""");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(fake.ApplyCount==0,"Failed verification must not place or withdraw text automatically.");
                    Require(doc.InsertedAt==null&&test.Controller.BannerReview==doc,"Failed verification must preserve Review.");
                    test.NoLargeWindow();
                });

                await Case("A failed insertion stays compact and an explicit retry succeeds once",async test=>
                {
                    var doc=Text("Retry this text");var fake=new FakeDelivery{FailuresRemaining=1};
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false},primaryFinal:false,completed:false);
                    await test.Handle(run,"""{"type":"transcript.final","text":"Retry this text"}""");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(fake.ApplyCount==1&&doc.InsertedAt==null,"Failed placement must not create a receipt.");
                    Require(test.Controller.BannerReview==doc,"Failed placement must leave its banner available.");
                    await test.Controller.ApplyBannerReview();
                    Require(fake.ApplyCount==2&&doc.InsertedAt!=null,"Explicit retry should confirm insertion.");
                    Require(test.Controller.BannerReview==null,"Successful retry must clear compact Review.");
                    var accepted=await test.Accept(doc);
                    Require(accepted.Success&&fake.ApplyCount==2,"An accepted receipt must prevent reinsertion.");
                    test.NoLargeWindow();
                });

                await Case("Final delivery allows bounded destination confirmation without the live deadline",async test=>
                {
                    var doc=Text("Long final result");
                    var fake=new FakeDelivery{BeforeConfirm=()=>Thread.Sleep(TimeSpan.FromMilliseconds(2300))};
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false},primaryFinal:false,completed:false);
                    await test.Handle(run,"""{"type":"transcript.final","text":"Long final result"}""");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(doc.InsertedAt!=null&&test.Controller.BannerReview==null,"Final confirmation was cancelled by the shorter live-delivery deadline.");
                    Require(fake.ApplyCount==1,"A slow final confirmation must not cause a second insertion.");
                });

                await Case("A confirmed insertion warning remains visible without permitting another insertion",async test=>
                {
                    const string warning="Text was inserted, but the original clipboard could not be restored.";
                    var doc=Text("Confirmed result");var fake=new FakeDelivery{SuccessWarning=warning};
                    var run=test.Run(doc,fake,new Settings{InsertAtCursor=false},primaryFinal:false,completed:false);
                    await test.Handle(run,"""{"type":"transcript.final","text":"Confirmed result"}""");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(doc.InsertedAt!=null&&test.Controller.Pending.Count==0,"A post-insertion warning must retain the insertion receipt.");
                    Require(test.Controller.BannerReview==null,"Confirmed output must not be offered as a failed insertion.");
                    Require(test.Controller.DeliveryMessage==warning&&test.Controller.DetailLabel==warning,"The successful insertion warning was hidden from the user.");
                    var accepted=await test.Accept(doc);
                    Require(accepted.Success&&fake.ApplyCount==1,"A warning must not allow the confirmed text to be inserted twice.");
                });

                await Case("Reopening resolved Review waits; Details acceptance clears matching compact state",async test=>
                {
                    var doc=Differences();doc.KeepRemaining();var fake=new FakeDelivery();
                    test.Bind(doc,fake);test.Controller.SaveReview(doc);test.Controller.OpenReview(doc);
                    Require(test.Controller.BannerReview==doc,"Existing Review should open compactly.");
                    Require(fake.ApplyCount==0&&doc.InsertedAt==null,"Opening an old resolved item must not auto-insert.");
                    var accepted=await test.Accept(doc);
                    Require(accepted.Success&&fake.ApplyCount==1,"Explicit Details callback should insert once.");
                    Require(test.Controller.BannerReview==null,"Details acceptance must clear the matching compact item.");
                    test.NoLargeWindow();
                });

                await Case("Cleanup of an older recording cannot replace a newer active recording",async test=>
                {
                    var oldDoc=Text("Old recording");var oldFake=new FakeDelivery();
                    var oldRun=test.Run(oldDoc,oldFake,new Settings{InsertAtCursor=false});
                    var currentDoc=Text("Current recording");var currentFake=new FakeDelivery();
                    var currentRun=test.Run(currentDoc,currentFake,new Settings{InsertAtCursor=false},primaryFinal:false,completed:false);
                    await test.Complete(oldRun);
                    Require(ReferenceEquals(Field<object>(test.Controller,"active"),currentRun),"Old cleanup changed the active run.");
                    Require(oldFake.ApplyCount==0&&currentFake.ApplyCount==0,"Old cleanup must not insert into either target.");
                    Require(test.Controller.BannerReview==null,"Old cleanup must not display Review over the current recording.");
                    test.NoLargeWindow();
                });

                await Case("A failed insertion receipt remains pending without reinserting",async test=>
                {
                    var doc=Text("Keep the insertion receipt");var fake=new FakeDelivery();
                    test.Bind(doc,fake);test.Controller.SaveReview(doc);test.Controller.OpenReview(doc);
                    FileStream? locked=null;
                    fake.BeforeConfirm=()=>locked=new FileStream(Path.Combine(test.Home,"data","review",doc.Id+".review"),
                        FileMode.Open,FileAccess.ReadWrite,FileShare.None);
                    try
                    {
                        await test.Controller.ApplyBannerReview();
                        Require(fake.ApplyCount==1&&doc.InsertedAt!=null,"Confirmed insertion must set the in-memory receipt.");
                        Require(test.Controller.Pending.Any(d=>d.Id==doc.Id&&d.InsertedAt!=null),"An unsaved receipt must remain pending.");
                        Require(Field<PendingReviews>(test.Controller,"pending").UnsavedIds.Contains(doc.Id),"The failed receipt save must be marked unsaved.");
                        var accepted=await test.Accept(doc);
                        Require(accepted.Success&&fake.ApplyCount==1,"Retrying a confirmed receipt must not insert again.");
                    }
                    finally{locked?.Dispose();}
                    test.NoLargeWindow();
                });

                await Case("A Changed handler failure releases both insertion guards",async test=>
                {
                    var doc=Text("Recover after notification failure");var fake=new FakeDelivery();
                    test.Bind(doc,fake);test.Controller.SaveReview(doc);test.Controller.OpenReview(doc);
                    foreach(var compact in new[]{false,true})
                    {
                        var raised=false;
                        Action throwOnce=()=>{if(raised)return;raised=true;throw new InvalidOperationException("Synthetic Changed handler failure.");};
                        test.Controller.Changed+=throwOnce;
                        try
                        {
                            try
                            {
                                if(compact)await test.Controller.ApplyBannerReview();
                                else await test.Accept(doc);
                                throw new InvalidOperationException("The synthetic notification failure was not observed.");
                            }
                            catch(InvalidOperationException ex) when(ex.Message=="Synthetic Changed handler failure."){}
                        }
                        finally{test.Controller.Changed-=throwOnce;}
                        Require(!test.Controller.IsApplyingReview,"A notification failure left compact Review locked.");
                        Require(Field<HashSet<string>>(test.Controller,"inserting").Count==0,"A notification failure left insertion locked.");
                        Require(fake.ApplyCount==0,"Notification failure should not insert text.");
                    }
                    await test.Controller.ApplyBannerReview();
                    Require(fake.ApplyCount==1&&doc.InsertedAt!=null,"The released guards must allow a deliberate retry.");
                    test.NoLargeWindow();
                });
                await Case("History opens the latest pending manual draft",async test=>
                {
                    var doc=Text("Original wording");test.Controller.SaveReview(doc);
                    var stale=Clone(doc);
                    doc.ManualFinalDraft="My latest manual draft";test.Controller.SaveReview(doc);
                    test.Controller.OpenReview(stale);
                    Require(test.Controller.BannerReview?.OutputText=="My latest manual draft","Detached History replaced the latest pending manual draft.");
                    test.Controller.SaveReview(stale);
                    Require(new ReviewStore(Path.Combine(test.Home,"data","review")).Load().Single().OutputText=="My latest manual draft","A stale save overwrote protected pending text.");
                    await Task.CompletedTask;
                });

                await Case("Restart honors a History receipt when pending receipt saving failed",async test=>
                {
                    var doc=Text("Already delivered once");var stale=Clone(doc);var fake=new FakeDelivery();
                    Field<HistoryStore>(test.Controller,"historyStore").Add(doc,new byte[4]);
                    Set(test.Controller,"<History>k__BackingField",Field<HistoryStore>(test.Controller,"historyStore").Load());
                    test.Bind(doc,fake);test.Controller.SaveReview(doc);test.Controller.OpenReview(doc);
                    FileStream? locked=null;
                    fake.BeforeConfirm=()=>locked=new FileStream(Path.Combine(test.Home,"data","review",doc.Id+".review"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
                    try{await test.Controller.ApplyBannerReview();}
                    finally{locked?.Dispose();}
                    Require(doc.InsertedAt!=null,"Fixture insertion did not receive a receipt.");
                    Require(new ReviewStore(Path.Combine(test.Home,"data","review")).Load().Single().InsertedAt==null,"Fixture must leave the old pending receipt on disk.");
                    await using var restarted=new TestContext(test.Home);
                    Require(restarted.Controller.Pending.Single().InsertedAt!=null,"Restart ignored the durable History receipt.");
                    var second=new FakeDelivery();restarted.Bind(stale,second);
                    var accepted=await restarted.Accept(stale);
                    Require(accepted.Success&&second.ApplyCount==0,"A stale pre-insertion snapshot was delivered twice after restart.");
                });

                await Case("Stale saves cannot erase an inserted receipt",async test=>
                {
                    var doc=Text("One insertion only");var stale=Clone(doc);var fake=new FakeDelivery();
                    Field<HistoryStore>(test.Controller,"historyStore").Add(doc,new byte[4]);
                    Set(test.Controller,"<History>k__BackingField",Field<HistoryStore>(test.Controller,"historyStore").Load());
                    test.Bind(doc,fake);test.Controller.SaveReview(doc);test.Controller.OpenReview(doc);
                    await test.Controller.ApplyBannerReview();
                    test.Controller.SaveReview(stale);
                    var saved=new ReviewStore(Path.Combine(test.Home,"data","review")).Load().Single();
                    Require(saved.InsertedAt!=null,"A stale save erased the insertion receipt.");
                    Require(Field<HistoryStore>(test.Controller,"historyStore").Load().Single().Document.InsertedAt!=null,"A stale save erased the History receipt.");
                    var accepted=await test.Accept(stale);
                    Require(accepted.Success&&fake.ApplyCount==1,"A stale object bypassed the insertion receipt.");
                });

                await Case("An active clipboard reservation blocks exit before recovery becomes pending",async test=>
                {
                    var doc=Text("Protected while native confirmation is stalled");test.Controller.SaveReview(doc);
                    using var transaction=(ClipboardTransaction)Activator.CreateInstance(typeof(ClipboardTransaction),true)!;
                    typeof(ClipboardRecovery).GetMethod("Reserve",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[transaction]);
                    Require(!ClipboardRecovery.HasPending,"An active operation must not become an explicit recovery action prematurely.");
                    var exit=(Task)typeof(AppController).GetMethod("PrepareForExit",Members)!.Invoke(test.Controller,null)!;
                    await exit;
                    Require(!Field<bool>(test.Controller,"exiting"),"Exit proceeded while a native clipboard delivery still owned its backup.");
                    Require(!Field<System.Windows.Forms.ToolStripMenuItem>(test.Controller,"clipboardRecoveryAction").Enabled,"Recovery was enabled while the native operation was still active.");
                    Require(new ReviewStore(Path.Combine(test.Home,"data","review")).Load().Single().Primary=="Protected while native confirmation is stalled","Blocked Exit lost protected pending text.");
                    Require(test.Controller.ConnectionMessage.Contains("still",StringComparison.OrdinalIgnoreCase),"Blocked Exit did not explain the still-active clipboard operation.");
                });

                await Case("Pending clipboard recovery blocks exit and preserves protected Review",async test=>
                {
                    var doc=Text("Protected pending dictation");test.Controller.SaveReview(doc);
                    // An empty, unchanged transaction owns no user clipboard data.
                    // Holding it exercises the real recovery lease and controller guard
                    // without reading, replacing or restoring the system clipboard.
                    var transaction=(ClipboardTransaction)Activator.CreateInstance(typeof(ClipboardTransaction),true)!;
                    ClipboardRecovery.Hold(transaction);
                    try
                    {
                        var exit=(Task)typeof(AppController).GetMethod("PrepareForExit",Members)!.Invoke(test.Controller,null)!;
                        await exit;
                        Require(!Field<bool>(test.Controller,"exiting"),"Normal Exit proceeded while the original clipboard backup was still held.");
                        Require(ClipboardRecovery.HasPending,"Exit discarded the pending clipboard recovery lease.");
                        Require(test.Controller.DeliveryMessage==ClipboardRecovery.Message&&test.Controller.ShowBanner,"The held clipboard recovery was not visible to the user.");
                        Require(new ReviewStore(Path.Combine(test.Home,"data","review")).Load().Single().Primary=="Protected pending dictation","Blocked Exit lost protected pending text.");
                    }
                    finally
                    {
                        var recovery=ClipboardRecovery.RestoreAfterInspection();
                        Require(recovery.Succeeded&&!ClipboardRecovery.HasPending,"The synthetic recovery lease did not release cleanly.");
                    }
                });

                await Case("Orderly exit awaits older recording cleanup and protected audio saving",async test=>
                {
                    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var doc=Text("Finishing an older cancelled run");
                    async Task Cleanup(){await release.Task;Field<HistoryStore>(test.Controller,"historyStore").Add(doc,new byte[4]);}
                    var cleanup=Cleanup();
                    var tasks=typeof(AppController).GetField("recordingTasks",Members)?.GetValue(test.Controller) as HashSet<Task>;
                    var method=typeof(AppController).GetMethod("PrepareForExit",Members);
                    if(tasks==null||method==null){release.SetResult();await cleanup;Require(false,"Exit has no retained recording cleanup to await.");}
                    tasks!.Add(cleanup);
                    var exit=(Task)method!.Invoke(test.Controller,null)!;
                    Require(!exit.IsCompleted,"Exit finished before recording cleanup.");
                    release.SetResult();await exit;
                    Require(Field<HistoryStore>(test.Controller,"historyStore").Load().Single().Id==doc.Id,"Exit completed before protected History was persisted.");
                    Require(Field<HistoryStore>(test.Controller,"historyStore").ReadAudio(doc.Id).Length==48,"Original synthetic PCM was not retained.");
                });
                exitCode=results.All(r=>r.Passed)?0:1;
            }
            catch(Exception ex){results.Add(new("Probe setup",false,0,ex.ToString()));}
            finally
            {
                Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",originalHome);
                var payload=new{Fixture="Local Dictation CompactFlowProbe",StartedUtc=started,CompletedUtc=DateTimeOffset.UtcNow,
                    FakeDeliveryOnly=true,NoMicrophoneCapture=true,NoApiConnection=true,Passed=exitCode==0,Results=results};
                var json=JsonSerializer.Serialize(payload,new JsonSerializerOptions{WriteIndented=true});
                Console.WriteLine(json);
                if(report!=null){Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,json);}
                var fullRoot=Path.GetFullPath(root);var tempRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                Console.Error.WriteLine("CompactFlowProbe cleanup: removing test-owned temporary data.");
                try
                {
                    if(fullRoot.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(fullRoot).StartsWith("LocalDictation-CompactFlowProbe-",StringComparison.Ordinal)&&Directory.Exists(fullRoot))
                        await Task.Run(()=>Directory.Delete(fullRoot,true)).WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch(Exception ex){Console.Error.WriteLine("Temporary cleanup did not finish: "+ex.GetType().Name);}
                Console.Error.WriteLine("CompactFlowProbe cleanup complete; exiting test host with code "+exitCode+".");
                // The fixture intentionally skips App.OnStartup. Do not enter production WPF/WinForms shutdown;
                // all controllers were disposed above, and the test host must terminate deterministically.
                Environment.Exit(exitCode);
            }

            async Task Case(string name,Func<TestContext,Task> check)
            {
                var clock=Stopwatch.StartNew();
                try
                {
                    await using var test=new TestContext(Path.Combine(root,Guid.NewGuid().ToString("N")));
                    await check(test);
                    results.Add(new(name,true,clock.Elapsed.TotalMilliseconds,"Passed"));
                }
                catch(Exception ex){results.Add(new(name,false,clock.Elapsed.TotalMilliseconds,ex.ToString()));}
            }
        }));
        Dispatcher.Run();return exitCode;
    }

    static ReviewDocument Clone(ReviewDocument doc)=>JsonSerializer.Deserialize<ReviewDocument>(JsonSerializer.Serialize(doc))!;
    static ReviewDocument Text(string text)
    {var doc=new ReviewDocument();doc.SetPrimary(text);return doc;}
    static ReviewDocument Differences()=>new()
    {
        Primary="The original text.",Verification="The revised wording.",VerificationModel="verify/model",VerificationCompleted=true,
        Pieces=[new("before","The ","The ",false),new("first","original","revised",true),new("middle"," "," ",false),new("second","text.","wording.",true)]
    };
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static async Task Until(Func<bool> condition)
    {
        var deadline=Stopwatch.StartNew();
        while(!condition())
        {if(deadline.Elapsed>TimeSpan.FromSeconds(5))throw new TimeoutException("The controller action did not complete.");await Task.Delay(10);}
    }
    static T Field<T>(object target,string name)=>(T)(target.GetType().GetField(name,Members)!.GetValue(target)!);
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Members)!.SetValue(target,value);
    sealed record CaseResult(string Name,bool Passed,double Milliseconds,string Detail);

    sealed class TestContext : IAsyncDisposable
    {
        public AppController Controller {get;}
        public string Home {get;}
        readonly List<object> runs=[];
        public TestContext(string home)
        {
            Home=home;
            Environment.SetEnvironmentVariable("LOCAL_DICTATION_HOME",home);
            Controller=new AppController(true);
            foreach(var name in new[]{"timer","reconnectTimer","resumeTimer"})Field<DispatcherTimer>(Controller,name).Stop();
        }
        public void Bind(ReviewDocument document,FakeDelivery delivery)=>Field<Dictionary<string,ITextDelivery>>(Controller,"destinations")[document.Id]=delivery;
        public object Run(ReviewDocument document,FakeDelivery delivery,Settings settings,bool primaryFinal=true,bool completed=true)
        {
            var run=Activator.CreateInstance(RunType,Members,null,[settings],null)!;runs.Add(run);
            Set(run,"Review",document);Set(run,"Text",document.Primary);Set(run,"Delivery",delivery);
            Set(run,"PrimaryFinal",primaryFinal);Set(run,"Completed",completed);Set(run,"NormalCompletion",completed);
            Set(run,"Recording",false);Set(run,"Closing",true);Set(run,"NeedsReview",true);
            Set(run,"DestinationChecked",true);
            Bind(document,delivery);Set(Controller,"active",run);return run;
        }
        public async Task Handle(object run,string json)
        {
            using var parsed=JsonDocument.Parse(json);
            await (Task)typeof(AppController).GetMethod("Handle",Members)!.Invoke(Controller,[run,parsed.RootElement.Clone()])!;
        }
        public Task Complete(object run)=>(Task)typeof(AppController).GetMethod("CompleteRun",Members)!.Invoke(Controller,[run])!;
        public Task<ReviewInsertionResult> Accept(ReviewDocument doc)=>(Task<ReviewInsertionResult>)typeof(AppController).GetMethod("AcceptReview",Members)!.Invoke(Controller,[doc])!;
        public void NoLargeWindow()=>Require(Field<ReviewWindow?>(Controller,"reviewWindow")==null,"A large Review window opened unexpectedly.");
        public async ValueTask DisposeAsync()
        {
            Controller.Dispose();
            Field<BannerWindow>(Controller,"banner").Close();
            Field<ReviewWindow?>(Controller,"reviewWindow")?.Close();
            foreach(var run in runs)
            {
                await Field<SessionStream>(run,"Stream").DisposeAsync();
                await Field<MicrophoneCapture>(run,"Capture").DisposeAsync();
                Field<CancellationTokenSource>(run,"Cancel").Dispose();
            }
        }
    }

    sealed class FakeDelivery : ITextDelivery
    {
        public nint Window=>0;
        public bool IsConflict {get;private set;}
        public string Reason {get;private set;}="";
        public string InsertedText {get;private set;}="";
        public int ApplyCount {get;private set;}
        public bool LastRestoreFocus {get;private set;}
        public int FailuresRemaining {get;set;}
        public string SuccessWarning {get;set;}="";
        public Action? BeforeConfirm {get;set;}
        public void Invalidate()=>IsConflict=true;
        public void Suspend(){}
        public bool OwnsProgrammaticFocus(nint root)=>false;
        public bool Apply(string next,CancellationToken token,bool restoreFocus=false)
        {
            token.ThrowIfCancellationRequested();ApplyCount++;LastRestoreFocus=restoreFocus;
            if(FailuresRemaining>0){FailuresRemaining--;Reason="Synthetic destination rejection.";return false;}
            if(IsConflict){Reason="Synthetic invalidated destination.";return false;}
            BeforeConfirm?.Invoke();
            Reason=SuccessWarning;InsertedText=next;return true;
        }
    }
}
