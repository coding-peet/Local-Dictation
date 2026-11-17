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

                await Case("Outlook keeps live partials in the banner and inserts once after Finish",async test=>
                {
                    var doc=Text("");var original=new FakeDelivery();
                    var settings=new Settings{Live=true,InsertAtCursor=true};settings.Save();
                    test.Controller.SaveSettings(settings);
                    var run=test.Run(doc,original,settings,primaryFinal:false,completed:false,processName:"olk");
                    Set(run,"Recording",true);Set(run,"Closing",false);
                    var exposedReview=false;
                    test.Controller.Changed+=()=>exposedReview|=test.Controller.BannerReview!=null;
                    await test.Handle(run,"""{"type":"transcript.updated","text":"First Outlook draft"}""");
                    await test.Handle(run,"""{"type":"transcript.updated","text":"Growing Outlook draft"}""");
                    await test.Deliver(run);
                    Require(original.ApplyCount==0,"Outlook partial updates or resume wrote into the destination before Finish.");
                    Require(test.Controller.LatestText=="Growing Outlook draft"&&test.Controller.LiveTranscription,"Outlook suppression stopped the live transcript.");
                    var banner=Field<BannerWindow>(test.Controller,"banner");banner.Refresh();
                    var status=Field<System.Windows.Controls.TextBlock>(banner,"detail").Text;
                    Require(status.Contains("Outlook",StringComparison.Ordinal)&&status.Contains("Finish",StringComparison.Ordinal)&&!status.Contains("Review mode",StringComparison.Ordinal),"Outlook recording status hides its automatic final delivery.");
                    Require(settings.InsertAtCursor&&test.Controller.Settings.InsertAtCursor&&Settings.Load().InsertAtCursor,"The Outlook policy changed the saved insertion preference.");
                    test.Controller.Stop();
                    Require(ReferenceEquals(Field<ITextDelivery>(run,"Delivery"),original),"Finish lost the original Outlook destination.");
                    await test.Handle(run,"""{"type":"transcript.final","text":"The complete Outlook dictation."}""");
                    Require(original.ApplyCount==0,"Outlook final text was inserted before successful session completion.");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(!exposedReview,"Successful Outlook completion exposed an unnecessary interactive Review.");
                    Require(doc.InsertedAt!=null&&original.ApplyCount==1&&original.InsertedText=="The complete Outlook dictation."&&original.LastRestoreFocus,"Outlook completion did not insert the complete text once at its original destination.");
                    await test.Controller.ApplyBannerReview();
                    Require(original.ApplyCount==1&&Settings.Load().InsertAtCursor,"Repeated completion inserted again or changed the saved preference.");
                    test.NoLargeWindow();
                });

                await Case("Classic Outlook verification differences wait for choices before one final insertion",async test=>
                {
                    var doc=Text("");doc.VerificationModel="verify/model";var original=new FakeDelivery();
                    var run=test.Run(doc,original,new Settings{Live=true,InsertAtCursor=true,VerifyAfterRecording=true},primaryFinal:false,completed:false,processName:"OUTLOOK");
                    Set(run,"Recording",true);Set(run,"Closing",false);
                    await test.Handle(run,"""{"type":"transcript.updated","text":"The original text."}""");
                    test.Controller.Stop();
                    await test.Handle(run,"""{"type":"transcript.final","text":"The original text."}""");
                    await test.Handle(run,"""{"type":"verification.started"}""");
                    await test.Handle(run,"""{"type":"verification.completed","verification":"The revised wording.","pieces":[{"id":"prefix","primary":"The ","verification":"The ","different":false},{"id":"first","primary":"original","verification":"revised","different":true},{"id":"gap","primary":" ","verification":" ","different":false},{"id":"second","primary":"text","verification":"wording","different":true},{"id":"end","primary":".","verification":".","different":false}]}""");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(original.ApplyCount==0&&test.Controller.BannerReview==doc,"Outlook verification inserted or withdrew text before wording choices.");
                    test.Controller.ChooseBannerPiece("verification");
                    Require(original.ApplyCount==0&&test.Controller.SelectedReviewPieceId=="second","Outlook inserted before the last wording choice.");
                    test.Controller.ChooseBannerPiece("primary");
                    await Until(()=>doc.InsertedAt!=null&&!test.Controller.IsApplyingReview);
                    Require(original.ApplyCount==1&&original.InsertedText=="The revised text."&&original.LastRestoreFocus,"Outlook choices did not insert the complete reviewed text once at the captured destination.");
                    test.NoLargeWindow();
                });

                foreach(var failure in new[]{"verification.failed","verification.cancelled"})
                await Case($"Outlook {failure} exposes its blocking reason before completion",async test=>
                {
                    var doc=Text("");doc.VerificationModel="verify/model";var original=new FakeDelivery();
                    var run=test.Run(doc,original,new Settings{Live=true,InsertAtCursor=true,VerifyAfterRecording=true},primaryFinal:false,completed:false,processName:"olk");
                    await test.Handle(run,"""{"type":"transcript.final","text":"The unverified Outlook dictation."}""");
                    await test.Handle(run,"""{"type":"verification.started"}""");
                    await test.Handle(run,JsonSerializer.Serialize(new{type=failure}));
                    Require(test.Controller.IsBusy&&doc.Reason.Contains("did not finish",StringComparison.Ordinal),"The verification failure did not remain active with a blocking reason.");
                    Require(test.Controller.DeliveryMessage==doc.Reason,"The Outlook insertion hint hides the active verification failure or cancellation reason.");
                    Require(original.ApplyCount==0,"Failed Outlook verification inserted text.");
                    await test.Handle(run,"""{"type":"session.completed"}""");
                    await test.Complete(run);
                    Require(doc.InsertedAt==null&&original.ApplyCount==0&&test.Controller.BannerReview==doc,"Failed or cancelled Outlook verification did not retain explicit Review.");
                });

                await Case("Other applications retain live cursor delivery after an Outlook session",async test=>
                {
                    var outlookDoc=Text("");var outlook=new FakeDelivery();
                    var run=test.Run(outlookDoc,outlook,new Settings{Live=true,InsertAtCursor=true},primaryFinal:false,completed:false,processName:"olk");
                    await test.Handle(run,"""{"type":"transcript.updated","text":"Outlook preview"}""");
                    await test.Handle(run,"""{"type":"session.cancelled"}""");
                    await test.Complete(run);
                    Require(outlook.ApplyCount==0,"Cancelled Outlook text was inserted.");
                    var otherDoc=Text("");var other=new FakeDelivery();
                    var next=test.Run(otherDoc,other,new Settings{Live=true,InsertAtCursor=true},primaryFinal:false,completed:false,processName:"notepad");
                    await test.Handle(next,"""{"type":"transcript.updated","text":"Live text in another application"}""");
                    Require(other.ApplyCount==1&&other.InsertedText=="Live text in another application","Outlook's session policy leaked into the next application's live delivery.");
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

                await Case("Idle recovery Close hides the banner and ignores its older queued notification",async test=>
                {
                    var doc=Text("Pending text survives hiding clipboard recovery");test.Controller.SaveReview(doc);test.Controller.OpenReview(doc);
                    using var transaction=(ClipboardTransaction)Activator.CreateInstance(typeof(ClipboardTransaction),true)!;
                    ClipboardRecovery.Hold(transaction);
                    try
                    {
                        // Hold posts its notification, then Close happens before that callback runs.
                        test.Controller.Cancel();
                        Require(!test.Controller.ShowBanner,"Close left the idle recovery banner visible.");
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Require(!test.Controller.ShowBanner&&!Field<BannerWindow>(test.Controller,"banner").IsVisible,"An older queued recovery notification undid the later Close.");
                        Require(ClipboardRecovery.HasPending&&ClipboardRecovery.HasUnsettledTransaction,"Close released the held clipboard backup.");
                        Require(test.Controller.BannerReview==null&&test.Controller.Pending.Single().Id==doc.Id,"Close discarded protected pending Review.");
                        Require(new ReviewStore(Path.Combine(test.Home,"data","review")).Load().Single().Primary==doc.Primary,"Close changed protected saved Review.");
                    }
                    finally{Require(ClipboardRecovery.RestoreAfterInspection().Succeeded,"The empty recovery lease did not release.");}
                });

                await Case("Deliberate Exit resurfaces dismissed recovery and remains blocked",async test=>
                {
                    var doc=Text("Still protected after a deliberate Exit");test.Controller.SaveReview(doc);
                    using var transaction=(ClipboardTransaction)Activator.CreateInstance(typeof(ClipboardTransaction),true)!;
                    ClipboardRecovery.Hold(transaction);
                    try
                    {
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        test.Controller.Cancel();
                        var exit=(Task<bool>)typeof(AppController).GetMethod("PrepareForExit",Members)!.Invoke(test.Controller,null)!;
                        Require(!await exit&&!Field<bool>(test.Controller,"exiting"),"Dismissed recovery allowed normal Exit.");
                        Require(test.Controller.ShowBanner&&Field<BannerWindow>(test.Controller,"banner").IsVisible,"Deliberate Exit did not resurface required recovery.");
                        Require(ClipboardRecovery.HasPending&&ClipboardRecovery.HasUnsettledTransaction,"Blocked Exit discarded the backup.");
                        Require(new ReviewStore(Path.Combine(test.Home,"data","review")).Load().Single().Primary==doc.Primary,"Blocked Exit lost protected Review.");
                        test.Controller.Cancel();
                        Require(!test.Controller.ShowBanner,"Resurfaced recovery could not be hidden again.");
                    }
                    finally{Require(ClipboardRecovery.RestoreAfterInspection().Succeeded,"The empty recovery lease did not release.");}
                });

                await Case("A new recovery event resurfaces a previously dismissed banner",async test=>
                {
                    using var first=(ClipboardTransaction)Activator.CreateInstance(typeof(ClipboardTransaction),true)!;
                    using var second=(ClipboardTransaction)Activator.CreateInstance(typeof(ClipboardTransaction),true)!;
                    ClipboardRecovery.Hold(first);
                    try
                    {
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        test.Controller.Cancel();
                        Require(!test.Controller.ShowBanner,"The first recovery was not dismissed.");
                        Require(ClipboardRecovery.RestoreAfterInspection().Succeeded,"The first empty lease did not release.");
                        ClipboardRecovery.Hold(second);
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Require(test.Controller.ShowBanner&&Field<BannerWindow>(test.Controller,"banner").IsVisible,"A genuinely new recovery stayed hidden.");
                        Require(ClipboardRecovery.HasPending&&ClipboardRecovery.HasUnsettledTransaction,"The new recovery backup was not retained.");
                    }
                    finally{Require(ClipboardRecovery.RestoreAfterInspection().Succeeded,"The empty recovery lease did not release.");}
                });

                await Case("One restore action releases idle recovery without a second confirmation",async test=>
                {
                    var doc=Text("Pending Review survives clipboard recovery");test.Controller.SaveReview(doc);test.Controller.OpenReview(doc);
                    using var transaction=(ClipboardTransaction)Activator.CreateInstance(typeof(ClipboardTransaction),true)!;
                    ClipboardRecovery.Hold(transaction);
                    try
                    {
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        var banner=Field<BannerWindow>(test.Controller,"banner");banner.Refresh();
                        var action=Field<System.Windows.Controls.Button>(banner,"action");
                        Require(action.IsEnabled,"Idle recovery does not offer its Restore action.");
                        Set(test.Controller,"restoringClipboard",true);banner.Refresh();
                        Require(!action.IsEnabled,"Restore remained enabled while recovery was running.");
                        Set(test.Controller,"restoringClipboard",false);banner.Refresh();
                        typeof(BannerWindow).GetMethod("MainAction",Members)!.Invoke(banner,null);
                        for(var attempt=0;attempt<100&&ClipboardRecovery.HasPending;attempt++)await Task.Delay(20);
                        Require(!ClipboardRecovery.HasPending&&!ClipboardRecovery.HasUnsettledTransaction,"One Restore action did not complete recovery.");
                        Require(test.Controller.Pending.Single().Id==doc.Id,"Restoring the clipboard discarded the protected Review.");
                    }
                    finally{if(ClipboardRecovery.HasPending)Require(ClipboardRecovery.RestoreAfterInspection().Succeeded,"The empty recovery lease did not release.");}
                });

                await Case("Clipboard recovery keeps a long compact Review visible without duplicating its transcript",async test=>
                {
                    var text=string.Join(" ",Enumerable.Repeat("Long protected dictation remains available for deliberate review.",40));
                    var doc=Text(text);test.Controller.SaveReview(doc);test.Controller.OpenReview(doc);
                    using var transaction=(ClipboardTransaction)Activator.CreateInstance(typeof(ClipboardTransaction),true)!;
                    ClipboardRecovery.Hold(transaction);
                    try
                    {
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        var banner=Field<BannerWindow>(test.Controller,"banner");banner.Refresh();
                        Require(Field<System.Windows.Controls.Border>(banner,"preview").Visibility==Visibility.Collapsed,"Recovery duplicated the full compact Review in the ordinary preview.");
                        Require(Field<CompactReviewPanel>(banner,"reviewPanel").Visibility==Visibility.Visible,"Recovery hid the existing compact Review.");
                        var action=Field<System.Windows.Controls.Button>(banner,"action");
                        Require((string)action.Content=="Restore clipboard"&&action.IsEnabled,"Compact Review displaced explicit clipboard recovery.");
                        Require(test.Controller.BannerReview?.OutputText==text&&test.Controller.Pending.Single().OutputText==text,"Recovery changed the long pending draft.");
                        Require(new ReviewStore(Path.Combine(test.Home,"data","review")).Load().Single().OutputText==text,"Recovery changed the protected saved draft.");
                    }
                    finally{Require(ClipboardRecovery.RestoreAfterInspection().Succeeded,"The empty recovery lease did not release.");}
                });

                await Case("Recovery never displaces Finish or enables restore during an active run",async test=>
                {
                    using var transaction=(ClipboardTransaction)Activator.CreateInstance(typeof(ClipboardTransaction),true)!;
                    ClipboardRecovery.Hold(transaction);
                    try
                    {
                        var run=test.Run(Text("Synthetic unfinished recording"),new FakeDelivery(),new Settings(),completed:false);
                        Set(run,"Recording",true);
                        var banner=Field<BannerWindow>(test.Controller,"banner");banner.Refresh();
                        var action=Field<System.Windows.Controls.Button>(banner,"action");
                        Require((string)action.Content=="■  Finish"&&action.IsEnabled,"Recovery displaced the active recording's Finish action.");
                        typeof(BannerWindow).GetMethod("MainAction",Members)!.Invoke(banner,null);
                        Require(!test.Controller.IsRecording&&ClipboardRecovery.HasPending,"Finish restored the clipboard instead of stopping the synthetic recording.");
                        banner.Refresh();
                        Require(!action.IsEnabled,"Recovery enabled its primary action while the run was still finishing.");
                        var restore=typeof(AppController).GetMethod("RestoreClipboardAfterInspection",BindingFlags.Instance|BindingFlags.Public);
                        Require(restore!=null,"The controlled recovery action is unavailable.");
                        await (Task)restore!.Invoke(test.Controller,null)!;
                        Require(ClipboardRecovery.HasPending,"The public recovery action ignored an active unsettled run.");
                    }
                    finally{Require(ClipboardRecovery.RestoreAfterInspection().Succeeded,"The empty recovery lease did not release.");}
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
        public object Run(ReviewDocument document,FakeDelivery delivery,Settings settings,bool primaryFinal=true,bool completed=true,string? processName=null)
        {
            var run=Activator.CreateInstance(RunType,Members,null,[settings],null)!;runs.Add(run);
            Set(run,"Review",document);Set(run,"Text",document.Primary);Set(run,"Delivery",delivery);
            Set(run,"PrimaryFinal",primaryFinal);Set(run,"Completed",completed);Set(run,"NormalCompletion",completed);
            Set(run,"Recording",false);Set(run,"Closing",true);Set(run,"NeedsReview",true);
            Set(run,"DestinationChecked",true);
            Bind(document,delivery);Set(Controller,"active",run);
            if(processName!=null)
            {
                var configure=typeof(AppController).GetMethod("ConfigureDestination",Members);
                Require(configure!=null,"Destination capture has no application delivery policy setup.");
                configure!.Invoke(Controller,[run,delivery,processName]);
            }
            return run;
        }
        public async Task Handle(object run,string json)
        {
            using var parsed=JsonDocument.Parse(json);
            await (Task)typeof(AppController).GetMethod("Handle",Members)!.Invoke(Controller,[run,parsed.RootElement.Clone()])!;
        }
        public Task Complete(object run)=>(Task)typeof(AppController).GetMethod("CompleteRun",Members)!.Invoke(Controller,[run])!;
        public Task Deliver(object run)=>(Task)typeof(AppController).GetMethod("Deliver",Members)!.Invoke(Controller,[run,true])!;
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
        public bool HasTransportFailure=>false;
        public bool IsOriginalFieldCurrent(CancellationToken token){token.ThrowIfCancellationRequested();return !IsConflict;}
        public bool Apply(string next,CancellationToken token,DeliveryIntent intent)=>Apply(next,token,intent.RestoreFocus);
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
