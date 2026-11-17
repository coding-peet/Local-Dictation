using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace LocalDictation.App.Services;

[JsonConverter(typeof(JsonStringEnumConverter<MissingCharacterKind>))]
public enum MissingCharacterKind {CR,LF,Space,Nbsp,ZeroWidthSpace,WordJoiner,BOM,ObjectReplacement,Other}

public sealed record DeliveryTextMismatch(int? FirstMismatchOffset,int? SingleDeletedPosition,
    MissingCharacterKind? SingleDeletedKind,bool MissingAtOwnedBoundary);

public sealed record DeliveryFocusMismatch(int? ExpectedProcessId,int? ObservedProcessId,
    string? ExpectedIdentityHash,string? ObservedIdentityHash,long ExpectedWindow,long ObservedWindow);

/// <summary>Opt-in operational evidence. It never contains text, UI names, paths, audio, or clipboard values.</summary>
public sealed record DeliveryDiagnostic(string Phase,bool Final,int QueuedInputEvents,double Milliseconds,
    int BeforeLength,int RequestedLength,int? ExpectedLength,int? ObservedLength,int? ObservedCaret,
    bool? ExactText,bool? ExactCaret,bool? Unchanged,bool? LineEndingsEquivalent,bool? TerminalParagraphEquivalent,bool? NbspEquivalent,
    string Provider,int? ClipboardFormatCount,bool? ClipboardRestored,bool? ClipboardSuperseded,bool Conflict,string? FailureType=null,int? FailureCode=null,bool ClipboardRecoveryPending=false,
    int ReadbackRetries=0,string? LastReadbackStage=null,string? LastReadbackFailureType=null,int? LastReadbackFailureCode=null,
    string? LastInterruptionStage=null,DeliveryTextMismatch? TextMismatch=null,bool OutlookParagraphProjection=false,
    DeliveryFocusMismatch? FocusMismatch=null,int FocusRestoreChecks=0,bool? ProviderFocusRequested=null,string InputMethod="ClipboardPaste",int InputChunks=0,string? OwnedRangeStrategy=null)
{
    public static DeliveryFocusMismatch? DescribeFocusMismatch(string? expectedIdentity,string? observedIdentity,
        nint expectedWindow,nint observedWindow)
    {
        if(!DiagnosticJournal.Enabled)return null;
        try
        {
            static int? ProcessId(string? identity)
            {
                var separator=identity?.IndexOf(':')??-1;
                return separator>0&&int.TryParse(identity.AsSpan(0,separator),NumberStyles.None,CultureInfo.InvariantCulture,out var processId)&&processId>0
                    ?processId:null;
            }
            static string? IdentityHash(string? identity)=>identity==null?null:
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0,8));
            return new(ProcessId(expectedIdentity),ProcessId(observedIdentity),IdentityHash(expectedIdentity),IdentityHash(observedIdentity),
                expectedWindow.ToInt64(),observedWindow.ToInt64());
        }
        catch(Exception){return null;}
    }

    public static DeliveryTextMismatch DescribeTextMismatch(string expected,string observed,int ownedBoundary)
    {
        var shared=Math.Min(expected.Length,observed.Length);var first=0;
        while(first<shared&&expected[first]==observed[first])first++;
        int? firstMismatch=first==shared&&expected.Length==observed.Length?null:first;
        bool WithoutCharacterMatches(int position)=>position>=0&&position<expected.Length&&position<=observed.Length&&
            expected.AsSpan(0,position).SequenceEqual(observed.AsSpan(0,position))&&
            expected.AsSpan(position+1).SequenceEqual(observed.AsSpan(position));
        if(expected.Length!=observed.Length+1||!WithoutCharacterMatches(first))return new(firstMismatch,null,null,false);
        var kind=expected[first] switch
        {
            '\r'=>MissingCharacterKind.CR,'\n'=>MissingCharacterKind.LF,' '=>MissingCharacterKind.Space,
            '\u00a0'=>MissingCharacterKind.Nbsp,'\u200b'=>MissingCharacterKind.ZeroWidthSpace,
            '\u2060'=>MissingCharacterKind.WordJoiner,'\ufeff'=>MissingCharacterKind.BOM,
            '\ufffc'=>MissingCharacterKind.ObjectReplacement,_=>MissingCharacterKind.Other
        };
        // Repeated equal characters can admit several deletion positions. Report
        // the first mismatch and test the owned boundary independently.
        return new(firstMismatch,first,kind,WithoutCharacterMatches(ownedBoundary));
    }

    public void WriteIfEnabled()
    {
        if(!DiagnosticJournal.Enabled)return;
        DiagnosticJournal.Append("delivery-metadata.jsonl",new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,Delivery=this});
    }

    public static void WriteSessionIfEnabled()
    {
        if(!DiagnosticJournal.Enabled)return;
        try
        {
            var assembly=typeof(DeliveryDiagnostic).Assembly;
            var buildVersion=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??assembly.GetName().Version?.ToString();
            DiagnosticJournal.Session(new{Time=DateTimeOffset.UtcNow,ProcessId=Environment.ProcessId,BuildVersion=buildVersion});
        }
        catch(Exception){}
    }
}
