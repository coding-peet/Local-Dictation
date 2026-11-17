namespace LocalDictation.Core;

public readonly record struct DestinationStamp(long Window, long Generation);
public sealed class DestinationEpoch
{
    long generation;
    public void UserInput() => Interlocked.Increment(ref generation);
    public bool UserInput(long observedWindow,long nonActivatingWindow)
    {
        if(nonActivatingWindow!=0&&observedWindow==nonActivatingWindow)return false;
        UserInput();return true;
    }
    public DestinationStamp Capture(long window) => new(window,Volatile.Read(ref generation));
    public bool CanAdopt(DestinationStamp stamp,long window) =>
        stamp.Window==window && stamp.Generation==Volatile.Read(ref generation);
}

public sealed class AudioBudget
{
    readonly long maximum;
    public long Accepted {get;private set;}
    public bool Reached => Accepted>=maximum;
    public AudioBudget(long maximumBytes)
    {
        if(maximumBytes<=0||maximumBytes%2!=0)throw new ArgumentException("PCM16 budget must be positive and aligned.");
        maximum=maximumBytes;
    }
    public int Accept(int bytes)
    {
        if(bytes<0||bytes%2!=0)throw new ArgumentException("Expected aligned PCM16.");
        var accepted=(int)Math.Min(bytes,maximum-Accepted);
        Accepted+=accepted;return accepted;
    }
}

public enum InputCommand {None,Toggle,Cancel,Start,Stop}
public readonly record struct InputDecision(InputCommand Command,bool Suppress);
public sealed class InputCommands
{
    ShortcutMode mode=ShortcutMode.Toggle;
    public ShortcutMode Mode
    {
        get=>mode;
        set{if(mode==value)return;mode=value;Reset();}
    }
    public bool IsHoldingShortcut {get;private set;}
    bool capturingShortcut;
    public bool CapturingShortcut
    {
        get=>capturingShortcut;
        set{if(capturingShortcut==value)return;capturingShortcut=value;Reset();}
    }
    ModifierChord chord=new();
    ShortcutSpec shortcut=ShortcutSpec.Parse("Ctrl+Shift");
    readonly HashSet<int> down=[];
    readonly HashSet<int> holdDown=[];
    ShortcutSpec? holdGesture;
    int suppressedHoldKey;
    bool armed;
    public ShortcutSpec Shortcut {get=>shortcut;set {Reset();shortcut=value;}}
    void Reset()
    {
        // Keep a live hold and its original key state through configuration/capture changes.
        if(Mode==ShortcutMode.Hold&&holdGesture is null&&holdDown.Any(k=>IsShortcutKey(shortcut,k)))
            holdGesture=shortcut;
        down.Clear();armed=false;
        // Reconfiguration must retain ownership of a consumed Escape until its physical release.
        escapeConsumed=escapeConsumed&&holdDown.Contains(0x1B);chord=new();
    }
    bool escapeConsumed;
    public InputDecision Update(int key,bool pressed,bool sessionActive,bool shortcutEnabled)
    {
        var holdFirst=pressed&&holdDown.Add(key);
        var released=!pressed&&holdDown.Remove(key);
        if(Mode==ShortcutMode.Hold||holdGesture is not null)
            return UpdateHold(key,pressed,holdFirst,released,sessionActive,shortcutEnabled);
        if(CapturingShortcut)return new(InputCommand.None,false);
        bool toggle=false,suppress=false;
        if(shortcut.Key==0)toggle=chord.Update(key,pressed);
        else
        {
            var first=pressed&&down.Add(key);
            if(pressed&&key==shortcut.Key&&(armed||shortcutEnabled&&shortcut.Matches(down)))
            {if(first)armed=true;suppress=true;}
            if(!pressed)
            {
                var wasDown=down.Remove(key);
                if(key==shortcut.Key&&armed){toggle=wasDown;armed=false;suppress=true;}
            }
        }
        if(key==0x1B)
        {
            if(pressed&&sessionActive)
            {
                var first=!escapeConsumed;escapeConsumed=true;
                return new(first?InputCommand.Cancel:InputCommand.None,true);
            }
            if(!pressed&&escapeConsumed){escapeConsumed=false;return new(InputCommand.None,true);}
        }
        return new(toggle&&shortcutEnabled?InputCommand.Toggle:InputCommand.None,suppress);
    }

    InputDecision UpdateHold(int key,bool pressed,bool first,bool released,bool sessionActive,bool shortcutEnabled)
    {
        var suppress=suppressedHoldKey!=0&&key==suppressedHoldKey;
        if(!pressed&&suppress)suppressedHoldKey=0;
        var command=InputCommand.None;
        if(holdGesture is not null)
        {
            if(!CapturingShortcut&&shortcutEnabled&&pressed&&key==holdGesture.Key&&MatchesHold(holdGesture))
            {if(first)suppressedHoldKey=key;suppress=true;}
            if(IsHoldingShortcut&&((released&&IsShortcutKey(holdGesture,key))||!MatchesHold(holdGesture)))
            {IsHoldingShortcut=false;command=InputCommand.Stop;}
            // A partial release/repress remains one gesture until its original keys are neutral.
            if(!holdDown.Any(k=>IsShortcutKey(holdGesture,k)))holdGesture=null;
        }
        if(!CapturingShortcut&&key==0x1B)
        {
            if(pressed&&sessionActive)
            {
                var cancel=!escapeConsumed;escapeConsumed=true;IsHoldingShortcut=false;
                return new(cancel?InputCommand.Cancel:InputCommand.None,true);
            }
            if(!pressed&&escapeConsumed){escapeConsumed=false;return new(command,true);}
        }
        if(command==InputCommand.Stop)return new(command,suppress);
        if(!CapturingShortcut&&Mode==ShortcutMode.Hold&&holdGesture is null&&first&&MatchesHold(shortcut))
        {
            holdGesture=shortcut;
            if(shortcutEnabled)
            {
                suppress=shortcut.Key!=0&&key==shortcut.Key;
                // Balance a primary key-down delivered before the modifiers with its key-up.
                if(suppress)suppressedHoldKey=shortcut.Key;
                if(!sessionActive){IsHoldingShortcut=true;return new(InputCommand.Start,suppress);}
            }
        }
        return new(InputCommand.None,suppress);
    }

    bool MatchesHold(ShortcutSpec value)=>value.Matches(holdDown)&&
        (value.Key==0||holdDown.Contains(value.Key))&&holdDown.All(k=>IsShortcutKey(value,k));
    static bool IsShortcutKey(ShortcutSpec value,int key)=>key switch
    {
        0x11 or 0xA2 or 0xA3=>value.Ctrl,
        0x10 or 0xA0 or 0xA1=>value.Shift,
        0x12 or 0xA4 or 0xA5=>value.Alt,
        _=>value.Key!=0&&key==value.Key
    };
}

public static class ModelEligibility
{
    public static string EffectiveLanguage(ModelInfo primary,string language)
    {
        var explicitLanguages=primary.Languages.Where(l=>l!="auto").ToArray();
        return language=="auto"&&explicitLanguages.Length==1?explicitLanguages[0]:language;
    }
    public static bool CanVerify(ModelInfo verifier,string language,ModelInfo primary,IReadOnlyList<string>? preferred=null)
    {
        if(!verifier.Installed||!verifier.RuntimeTested)return false;
        var effective=EffectiveLanguage(primary,language);
        if(effective!="auto")return verifier.Languages.Contains(effective);
        var candidates=primary.Languages.Where(l=>l!="auto"&&(preferred is not {Count:>0}||preferred.Contains(l))).ToArray();
        return candidates.Length>0&&candidates.All(verifier.Languages.Contains);
    }
}

/// <summary>Communication heartbeats never extend primary/verification absolute deadlines.</summary>
public sealed class SessionDeadline
{
    readonly Func<TimeSpan> clock;
    readonly TimeSpan progress,final,verification;
    TimeSpan communication;
    TimeSpan? absolute;
    bool started;
    public SessionDeadline(Func<TimeSpan> clock,TimeSpan startup,TimeSpan progress,TimeSpan final,TimeSpan verification)
    {
        this.clock=clock;this.progress=progress;this.final=final;this.verification=verification;
        communication=clock()+startup;
    }
    public void Finish()=>absolute=clock()+final;
    public void Observe(string kind)
    {
        if(kind=="session.started")started=true;
        if(!started)return;
        communication=clock()+progress;
        if(kind=="transcript.final"||kind is "verification.completed" or "verification.failed" or "verification.cancelled")
            absolute=clock()+TimeSpan.FromSeconds(30);
        if(kind=="verification.started")absolute=clock()+verification;
    }
    public TimeSpan Remaining()
    {
        var next=absolute.HasValue&&absolute.Value<communication?absolute.Value:communication;
        var remaining=next-clock();
        if(remaining<=TimeSpan.Zero)throw new TimeoutException(started?"Transcription progress deadline expired.":"The API did not acknowledge this session.");
        return remaining;
    }
}
