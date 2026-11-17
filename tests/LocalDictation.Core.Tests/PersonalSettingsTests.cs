using LocalDictation.Core;
using Xunit;

public class PersonalSettingsTests
{
    [Fact] public void ConfigurableShortcutIgnoresRepeatAndOtherKeys()
    {
        var input=new InputCommands {Shortcut=ShortcutSpec.Parse("Ctrl+Alt+Space")};
        input.Update(0x11,true,false,true);input.Update(0x12,true,false,true);
        Assert.True(input.Update(0x20,true,false,true).Suppress);
        Assert.Equal(InputCommand.None,input.Update(0x20,true,false,true).Command);
        Assert.Equal(InputCommand.Toggle,input.Update(0x20,false,false,true).Command);
        Assert.Equal(InputCommand.None,input.Update(0x20,false,false,true).Command);
    }

    [Fact] public void InvalidOrBareShortcutsAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>ShortcutSpec.Parse("A"));
        Assert.Throws<ArgumentException>(()=>ShortcutSpec.Parse("Ctrl+Escape"));
        Assert.Equal("Ctrl+Shift",ShortcutSpec.Parse("Ctrl+Shift").ToString());
    }
    [Fact] public void ShortcutRepeatStaysSuppressedAfterModifierRelease()
    {
        var input=new InputCommands {Shortcut=ShortcutSpec.Parse("Ctrl+Space")};
        input.Update(0x11,true,false,true);input.Update(0x20,true,false,true);
        input.Update(0x11,false,false,true);
        Assert.True(input.Update(0x20,true,false,true).Suppress);
        Assert.Equal(InputCommand.Toggle,input.Update(0x20,false,false,true).Command);
    }

    [Fact] public void SettingsRoundtripKeepsThemeAndShortcut()
    {
        var path=Path.Combine(Path.GetTempPath(),"ld-settings-"+Guid.NewGuid());
        try
        {
            new Settings {Theme="Dark",Shortcut="Ctrl+Alt+Space"}.Save(path);
            var restored=Settings.Load(path);
            Assert.Equal("Dark",restored.Theme);Assert.Equal("Ctrl+Alt+Space",restored.Shortcut);
        }
        finally {if(Directory.Exists(path))Directory.Delete(path,true);}
    }

    [Fact] public void HistoryKeepsAllTranscriptsAndRestoresOriginalAudio()
    {
        var path=Path.Combine(Path.GetTempPath(),"ld-history-"+Guid.NewGuid());
        try
        {
            var store=new HistoryStore(path);
            var pcm=new byte[]{0,1,2,3};
            var ids=new List<string>();
            for(var i=0;i<21;i++)
            {
                var doc=new ReviewDocument {Primary="Text "+i,Created=DateTimeOffset.UtcNow.AddMinutes(i)};
                ids.Add(doc.Id);store.Add(doc,pcm);
            }
            var entries=store.Load();
            Assert.Equal(21,entries.Count);
            Assert.Contains(entries,e=>e.Id==ids[0]);
            Assert.True(File.Exists(Path.Combine(path,ids[0]+".audio")));
            Assert.Equal("Text 20",entries[0].Document.Primary);
            var wave=store.ReadAudio(entries[0].Id);
            Assert.Equal("RIFF",System.Text.Encoding.ASCII.GetString(wave,0,4));
            Assert.Equal(pcm,wave[44..]);
            Assert.DoesNotContain("Text 20",System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(path,ids[20]+".history"))));
            store.Delete(ids[20]);Assert.Equal(20,store.Load().Count);
            Assert.Throws<ArgumentException>(()=>store.ReadAudio("../elsewhere"));
        }
        finally {if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    [Fact] public void MissingHistoryAudioDoesNotPreventStartupOrDeleteMetadata()
    {
        var path=Path.Combine(Path.GetTempPath(),"ld-history-missing-"+Guid.NewGuid());
        try
        {
            var doc=new ReviewDocument();doc.SetPrimary("recoverable text");
            var store=new HistoryStore(path);store.Add(doc,new byte[]{0,0});
            File.Delete(Path.Combine(path,doc.Id+".audio"));
            Assert.Equal("recoverable text",Assert.Single(store.Load()).Document.OutputText);Assert.Empty(store.Errors);
            Assert.True(File.Exists(Path.Combine(path,doc.Id+".history")));
        }
        finally {if(Directory.Exists(path))Directory.Delete(path,true);}
    }
}
