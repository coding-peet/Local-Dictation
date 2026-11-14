using System.Text.Json;
using LocalDictation.Core;
using Xunit;

public class AudioRetentionTests
{
    static readonly DateTimeOffset Now=new(2026,11,14,12,0,0,TimeSpan.Zero);
    sealed class Fixture:IDisposable
    {
        public string Root {get;}=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ld-retention-"+Guid.NewGuid().ToString("N")));
        public string History=>Path.Combine(Root,"history");
        public HistoryStore Store {get;}
        public Fixture(){Store=new(History);}
        public ReviewDocument Add(DateTimeOffset created,string text="Synthetic retention text")
        {var doc=new ReviewDocument{Created=created};doc.SetPrimary(text);Store.Add(doc,new byte[]{0,0,1,0});return doc;}
        public string Audio(ReviewDocument doc)=>Path.Combine(History,doc.Id+".audio");
        public string Text(ReviewDocument doc)=>Path.Combine(History,doc.Id+".history");
        public void Dispose()
        {
            var temp=Path.GetFullPath(Path.GetTempPath());
            if(!Path.GetFullPath(Root).Equals(Root,StringComparison.OrdinalIgnoreCase)||!Root.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(Root).StartsWith("ld-retention-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected synthetic test directory.");
            if(Directory.Exists(Root)){if((File.GetAttributes(Root)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Linked synthetic test directory.");Directory.Delete(Root,true);}
        }
    }

    [Fact] public void OldSettingsDefaultToSevenDaysAndChosenDurationRoundtrips()
    {
        using var f=new Fixture();Directory.CreateDirectory(f.Root);
        File.WriteAllText(Path.Combine(f.Root,"settings.json"),"{\"Theme\":\"Dark\"}");
        var old=Settings.Load(f.Root);Assert.Equal(7,old.AudioRetentionDays);Assert.Equal("Dark",old.Theme);
        old.AudioRetentionDays=30;old.Save(f.Root);Assert.Equal(30,Settings.Load(f.Root).AudioRetentionDays);
        old.AudioRetentionDays=0;old.Save(f.Root);Assert.Equal(0,Settings.Load(f.Root).AudioRetentionDays);
    }

    [Theory] [InlineData(-1)] [InlineData(3651)] [InlineData(int.MaxValue)]
    public void InvalidRetentionCannotOverwriteValidSettings(int days)
    {
        using var f=new Fixture();new Settings().Save(f.Root);var path=Path.Combine(f.Root,"settings.json");var before=File.ReadAllBytes(path);
        Assert.Throws<ArgumentException>(()=>new Settings{AudioRetentionDays=days}.Save(f.Root));Assert.Equal(before,File.ReadAllBytes(path));
        Assert.Throws<ArgumentOutOfRangeException>(()=>f.Store.ApplyAudioRetention(days,Now));
    }

    [Fact] public void SevenDayBoundaryExpiresOnlyOriginalAudioAndIsIdempotent()
    {
        using var f=new Fixture();var old=f.Add(Now.AddDays(-8));var boundary=f.Add(Now.AddDays(-7));var recent=f.Add(Now.AddDays(-7).AddTicks(1));var future=f.Add(Now.AddDays(1));
        var text=new[]{old,boundary,recent,future}.ToDictionary(d=>d.Id,d=>File.ReadAllBytes(f.Text(d)));
        Assert.Empty(f.Store.ApplyAudioRetention(7,Now));Assert.False(File.Exists(f.Audio(old)));Assert.False(File.Exists(f.Audio(boundary)));Assert.True(File.Exists(f.Audio(recent)));Assert.True(File.Exists(f.Audio(future)));
        Assert.Empty(f.Store.ApplyAudioRetention(7,Now));var entries=f.Store.Load();Assert.Equal(4,entries.Count);
        foreach(var entry in entries)Assert.Equal(text[entry.Id],File.ReadAllBytes(f.Text(entry.Document)));
        Assert.False(entries.Single(e=>e.Id==old.Id).HasAudio);Assert.True(entries.Single(e=>e.Id==recent.Id).HasAudio);
    }

    [Fact] public void ForeverAndFutureDatesNeverRemoveRecordings()
    {
        using var f=new Fixture();var old=f.Add(Now.AddYears(-10));var audio=File.ReadAllBytes(f.Audio(old));
        Assert.Empty(f.Store.ApplyAudioRetention(0,Now));Assert.Equal(audio,File.ReadAllBytes(f.Audio(old)));
        Assert.Empty(f.Store.ApplyAudioRetention(3650,DateTimeOffset.MinValue));Assert.True(File.Exists(f.Audio(old)));
    }

    [Fact] public void ExpiredTextRetainsEditsChoicesProvenanceReceiptAndPendingStorage()
    {
        using var f=new Fixture();var doc=f.Add(Now.AddDays(-10));doc.ManualFinalDraft="Edited synthetic text";doc.InsertedAt=Now.AddDays(-1);doc.PrimaryModel="synthetic/model";doc.Provenance=new("synthetic-session",new(doc.PrimaryModel,"revision-1"),null,doc.Created);doc.Choices["synthetic-piece"]=new("primary");f.Store.Update(doc);
        var reviews=new ReviewStore(Path.Combine(f.Root,"review"));var pending=new PendingReviews(reviews);Assert.True(pending.Save(doc));
        var historyBefore=File.ReadAllBytes(f.Text(doc));var pendingPath=Path.Combine(f.Root,"review",doc.Id+".review");var pendingBefore=File.ReadAllBytes(pendingPath);
        Assert.Empty(f.Store.ApplyAudioRetention(7,Now));Assert.Equal(historyBefore,File.ReadAllBytes(f.Text(doc)));Assert.Equal(pendingBefore,File.ReadAllBytes(pendingPath));
        var entry=Assert.Single(f.Store.Load());Assert.False(entry.HasAudio);Assert.Equal(doc.InsertedAt,entry.Document.InsertedAt);Assert.Equal(doc.Provenance,entry.Document.Provenance);Assert.Equal(doc.Choices["synthetic-piece"],entry.Document.Choices["synthetic-piece"]);
        var row=Assert.Single(DictationLibrary.Merge([entry],reviews.Load(),new HashSet<string>()));Assert.False(row.HasAudio);Assert.Equal(4/32000d,row.DurationSeconds);Assert.Equal("Edited synthetic text",row.Document.OutputText);Assert.Equal("Inserted",row.State);Assert.Equal("Text only",row.DurationLabel);
    }

    [Fact] public void MissingAudioStillAllowsTextUpdateAndPendingEditsTakePrecedence()
    {
        using var f=new Fixture();var old=f.Add(Now.AddDays(-10));Assert.Empty(f.Store.ApplyAudioRetention(7,Now));
        old.ManualFinalDraft="Updated after audio expiry";f.Store.Update(old);var loaded=Assert.Single(f.Store.Load());Assert.Equal(old.OutputText,loaded.Document.OutputText);Assert.False(loaded.HasAudio);Assert.Empty(f.Store.Errors);
        var draft=new ReviewDocument{Id=old.Id};draft.SetPrimary(old.Primary);draft.ManualFinalDraft="Current pending edit";
        var row=Assert.Single(DictationLibrary.Merge([loaded],[draft],new HashSet<string>{old.Id}));Assert.Same(draft,row.Document);Assert.False(row.HasAudio);Assert.True(row.IsUnsaved);Assert.True(row.NeedsReview);Assert.NotNull(row.DurationSeconds);
    }

    [Fact] public void CorruptOrOrphanRecordsArePreservedWithoutDeletingTheirAudio()
    {
        using var f=new Fixture();var corrupt=f.Add(Now.AddDays(-10));File.WriteAllBytes(f.Text(corrupt),new byte[]{1,2,3});
        var orphan=Path.Combine(f.History,Guid.NewGuid().ToString("N")+".audio");File.WriteAllBytes(orphan,new byte[]{4,5,6});
        var corruptAudio=File.ReadAllBytes(f.Audio(corrupt));Assert.Empty(f.Store.ApplyAudioRetention(7,Now));
        Assert.Equal(corruptAudio,File.ReadAllBytes(f.Audio(corrupt)));Assert.Equal(new byte[]{1,2,3},File.ReadAllBytes(f.Text(corrupt)));Assert.Equal(new byte[]{4,5,6},File.ReadAllBytes(orphan));Assert.Single(f.Store.Errors);
    }

    [Fact] public void LockedAudioIsReportedAndDoesNotBlockOtherExpiredEntries()
    {
        using var f=new Fixture();var blocked=f.Add(Now.AddDays(-10));var free=f.Add(Now.AddDays(-9));
        var text=File.ReadAllBytes(f.Text(blocked));
        using(var locked=new FileStream(f.Audio(blocked),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Assert.Equal(new[]{blocked.Id},f.Store.ApplyAudioRetention(7,Now));Assert.True(File.Exists(f.Audio(blocked)));Assert.False(File.Exists(f.Audio(free)));Assert.Equal(text,File.ReadAllBytes(f.Text(blocked)));
        }
        Assert.Empty(f.Store.ApplyAudioRetention(7,Now));Assert.False(File.Exists(f.Audio(blocked)));Assert.Equal(2,f.Store.Load().Count);
    }

    [Fact] public void AudioAvailabilityComesFromCurrentFilesRatherThanSerializedMetadata()
    {
        using var f=new Fixture();var doc=f.Add(Now);var original=Assert.Single(f.Store.Load());Assert.True(original.HasAudio);
        Assert.DoesNotContain("HasAudio",JsonSerializer.Serialize(original));File.Delete(f.Audio(doc));var textOnly=Assert.Single(f.Store.Load());Assert.False(textOnly.HasAudio);
        Assert.False(Assert.Single(DictationLibrary.Merge([textOnly],[],new HashSet<string>())).HasAudio);
    }
}
