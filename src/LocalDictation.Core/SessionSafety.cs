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

public enum InputCommand {None,Toggle,Cancel}
public readonly record struct InputDecision(InputCommand Command,bool Suppress);
public sealed class InputCommands
{
    ModifierChord chord=new();
    ShortcutSpec shortcut=ShortcutSpec.Parse("Ctrl+Shift");
    readonly HashSet<int> down=[];
    bool armed;
    public ShortcutSpec Shortcut {get=>shortcut;set {shortcut=value;down.Clear();armed=false;chord=new();}}
    bool escapeConsumed;
    public InputDecision Update(int key,bool pressed,bool sessionActive,bool shortcutEnabled)
    {
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
