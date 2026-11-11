using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalDictation.App.Services;

/// <summary>A local, short-lived paste payload. Unsupported clipboard formats fail before replacement.</summary>
public sealed class ClipboardTransaction : IDisposable
{
    const uint UnicodeText=13,Movable=2;
    const nuint MaximumBackupBytes=64*1024*1024;
    readonly object stateGate=new();
    readonly nint owner;
    readonly List<FormatCopy> saved=[];
    readonly byte[] marker=Guid.NewGuid().ToByteArray();
    readonly uint markerFormat=RegisterClipboardFormat("LocalDictation.ClipboardLease");
    byte[] payloadBytes=[];
    uint sequence;
    bool changed,finished,mayHaveQueued,acknowledged,restoreFailed;
    public int SavedFormatCount=>saved.Count;
    public bool WasRestored {get;private set;}
    public bool WasSuperseded {get;private set;}
    public bool RecoveryPending {get;internal set;}
    public string RecoveryMessage=>restoreFailed
        ?"The previous clipboard could not be fully restored. Its complete backup is held in memory. Keep Local Dictation open and use Restore previous clipboard."
        :"Paste was not confirmed. Dictation remains on the clipboard, and its previous contents are held in memory. Check the destination and stop any delayed paste before using Restore previous clipboard. Keep Local Dictation open until recovery is complete.";
    internal bool WasReplacedByAnotherOwner=>changed&&!finished&&GetClipboardOwner()!=owner;

    sealed class FormatCopy(uint format,nint handle,Action<nint> release) : IDisposable
    {
        public uint Format=format;
        public nint Handle=handle;
        public void Transfer()
        {
            if(SetClipboardData(Format,Handle)==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows could not restore a clipboard format.");
            Handle=0;
        }
        public void Dispose(){if(Handle!=0){release(Handle);Handle=0;}}
    }

    ClipboardTransaction(){owner=ClipboardOwner.Window;}

    public static ClipboardTransaction Begin(string text,Action validate)
    {
        var transaction=new ClipboardTransaction();
        ClipboardRecovery.Reserve(transaction);
        var payload=new List<FormatCopy>();
        try
        {
            transaction.payloadBytes=Encoding.Unicode.GetBytes(text+"\0");
            payload.Add(Bytes(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"),[1]));
            payload.Add(Bytes(RegisterClipboardFormat("CanIncludeInClipboardHistory"),[0,0,0,0]));
            payload.Add(Bytes(RegisterClipboardFormat("CanUploadToCloudClipboard"),[0,0,0,0]));
            payload.Add(Bytes(transaction.markerFormat,transaction.marker));
            payload.Add(Bytes(UnicodeText,transaction.payloadBytes));
            transaction.Open(validate,100);
            try
            {
                nuint backupBytes=0;uint format=0;
                while(true)
                {
                    SetLastError(0);format=EnumClipboardFormats(format);
                    if(format==0)
                    {
                        if(Marshal.GetLastWin32Error()!=0)throw new Win32Exception("The current clipboard formats could not be enumerated.");
                        break;
                    }
                    validate();
                    var handle=GetClipboardData(format);
                    if(handle==0)throw new InvalidOperationException("A clipboard format could not be preserved. The clipboard and destination were left untouched.");
                    transaction.saved.Add(Clone(format,handle,ref backupBytes));
                }
                validate();
                if(!EmptyClipboard())throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows could not prepare the paste clipboard.");
                transaction.changed=true;
                foreach(var copy in payload)copy.Transfer();
            }
            finally{CloseClipboard();transaction.sequence=GetClipboardSequenceNumber();}
            return transaction;
        }
        catch{transaction.Dispose();throw;}
        finally{foreach(var copy in payload)copy.Dispose();}
    }

    public void EnsureCurrent()
    {
        if(finished)throw new InvalidOperationException("The paste clipboard lease has ended.");
        Open(()=>{},100);
        try
        {
            // CloseClipboard and implicit format synthesis can advance the sequence.
            // The unique owner, lease marker and exact Unicode payload prove this is still our data.
            if(GetClipboardOwner()!=owner||!Contains(markerFormat,marker)||!Contains(UnicodeText,payloadBytes))
                throw new InvalidOperationException("The clipboard changed before paste. Your transcript is preserved.");
        }
        finally{CloseClipboard();sequence=GetClipboardSequenceNumber();}
    }
    static bool Contains(uint format,byte[] bytes)
    {
        var handle=GetClipboardData(format);if(handle==0||GlobalSize(handle)<(nuint)bytes.Length)return false;
        var memory=GlobalLock(handle);if(memory==0)return false;
        try{var actual=new byte[bytes.Length];Marshal.Copy(memory,actual,0,actual.Length);return actual.SequenceEqual(bytes);}
        finally{GlobalUnlock(handle);}
    }
    public void MarkMayHaveQueued()=>mayHaveQueued=true;
    public void MarkNoInputQueued(){if(!acknowledged)mayHaveQueued=false;}
    public void Acknowledge()=>acknowledged=true;

    void Open(Action validate,int milliseconds)
    {
        for(var elapsed=0;;elapsed+=10)
        {
            validate();if(OpenClipboard(owner))return;
            if(elapsed>=milliseconds)throw new Win32Exception(Marshal.GetLastWin32Error(),"The clipboard is busy. Try insertion again.");
            Thread.Sleep(10);
        }
    }
    public void Dispose()
    {
        lock(stateGate)
        {
            if(finished)return;
            if(mayHaveQueued&&!acknowledged){ClipboardRecovery.Hold(this);return;}
            RestoreCore();
        }
    }
    internal bool RestoreExplicitly(){lock(stateGate)return RestoreCore();}
    bool RestoreCore()
    {
        if(finished)return true;
        if(!changed){Complete();return true;}
        var attempt=new List<FormatCopy>();
        try
        {
            Open(()=>{},400);
            try
            {
                if(GetClipboardOwner()!=owner){WasSuperseded=true;Complete();return true;}
                // Preserve the COMPLETE saved originals until every format has transferred.
                // A failed attempt can therefore be retried without losing already-transferred formats.
                nuint backupBytes=0;
                foreach(var copy in saved)attempt.Add(Clone(copy.Format,copy.Handle,ref backupBytes));
                if(!EmptyClipboard())throw new Win32Exception("Windows could not restore the previous clipboard.");
                foreach(var copy in attempt)copy.Transfer();
                WasRestored=true;changed=false;
            }
            finally{CloseClipboard();sequence=GetClipboardSequenceNumber();}
            Complete();return true;
        }
        catch(Exception error) when(error is InvalidOperationException or Win32Exception)
        {
            restoreFailed=true;ClipboardRecovery.Hold(this);Diagnostics.Error("clipboard restore",error);return false;
        }
        finally{foreach(var copy in attempt)copy.Dispose();}
    }
    internal void ReleaseSuperseded(){lock(stateGate){if(finished)return;WasSuperseded=true;Complete();}}
    void Complete()
    {
        if(finished)return;
        finished=true;RecoveryPending=false;
        foreach(var copy in saved)copy.Dispose();
        ClipboardRecovery.Release(this);
    }

    // This independent owner survives AutomationWorker disposal. It has no visible UI.
    static class ClipboardOwner
    {
        static readonly Lazy<nint> window=new(Create);
        public static nint Window=>window.Value;
        static nint Create()
        {
            var ready=new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);nint handle=0;
            var thread=new Thread(()=>
            {
                try
                {
                    handle=CreateWindowEx(0,"STATIC",null,0,0,0,0,0,new nint(-3),0,0,0);
                    if(handle==0)throw new Win32Exception("Windows could not create the local clipboard owner.");
                }
                catch(Exception error){ready.TrySetException(error);}
                if(handle==0)return;
                ready.TrySetResult(handle);
                while(GetMessage(out var message,0,0,0)>0){TranslateMessage(ref message);DispatchMessage(ref message);}
                DestroyWindow(handle);
            }){IsBackground=true,Name="Local Dictation clipboard owner"};
            thread.SetApartmentState(ApartmentState.STA);thread.Start();
            try{return ready.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();}
            catch(Exception error){throw new InvalidOperationException("The clipboard owner did not start.",error);}
        }
    }

    static FormatCopy Bytes(uint format,byte[] bytes)
    {
        if(format==0)throw new Win32Exception("A local clipboard format could not be registered.");
        var memory=GlobalAlloc(Movable|0x40,(nuint)bytes.Length);
        if(memory==0)throw new Win32Exception("Clipboard backup allocation failed.");
        var locked=GlobalLock(memory);
        if(locked==0){GlobalFree(memory);throw new Win32Exception("Clipboard backup could not be locked.");}
        try{Marshal.Copy(bytes,0,locked,bytes.Length);}
        finally{GlobalUnlock(memory);}
        return new(format,memory,h=>GlobalFree(h));
    }

    static FormatCopy Clone(uint format,nint handle,ref nuint backupBytes)
    {
        if(format is 0x80 or >=0x200 and <=0x3ff)
            throw new InvalidOperationException("The clipboard contains an owner-managed format that cannot be preserved safely. The clipboard and destination were left untouched.");
        if(format is 2 or 0x82)
        {
            var copy=CopyImage(handle,0,0,0,0x2000);
            if(copy==0)throw new Win32Exception("The clipboard bitmap could not be preserved.");
            return new(format,copy,h=>DeleteObject(h));
        }
        if(format is 14 or 0x8e)
        {
            var copy=CopyEnhMetaFile(handle,null);
            if(copy==0)throw new Win32Exception("The clipboard enhanced metafile could not be preserved.");
            return new(format,copy,h=>DeleteEnhMetaFile(h));
        }
        if(format==9)
        {
            var count=GetPaletteEntries(handle,0,0,0);
            if(count==0||count>65535)throw new InvalidOperationException("The clipboard palette cannot be preserved safely.");
            var palette=Marshal.AllocHGlobal(checked(4+(int)count*4));
            try
            {
                Marshal.WriteInt16(palette,0,0x300);Marshal.WriteInt16(palette,2,(short)count);
                if(GetPaletteEntries(handle,0,count,palette+4)!=count)throw new Win32Exception("The clipboard palette could not be read.");
                var copy=CreatePalette(palette);
                if(copy==0)throw new Win32Exception("The clipboard palette could not be preserved.");
                return new(format,copy,h=>DeleteObject(h));
            }
            finally{Marshal.FreeHGlobal(palette);}
        }
        var size=GlobalSize(handle);
        if(size==0||size>MaximumBackupBytes-backupBytes)
            throw new InvalidOperationException("The clipboard format or backup size is not supported safely. The clipboard and destination were left untouched.");
        backupBytes+=size;
        var source=GlobalLock(handle);
        if(source==0)throw new Win32Exception("A clipboard format could not be preserved.");
        nint memory=0,metafile=0;
        try
        {
            memory=GlobalAlloc(Movable,size);
            if(memory==0)throw new Win32Exception("Clipboard backup allocation failed.");
            var destination=GlobalLock(memory);
            if(destination==0)throw new Win32Exception("Clipboard backup could not be locked.");
            try
            {
                CopyMemory(destination,source,size);
                if(format is 3 or 0x83)
                {
                    var offset=IntPtr.Size==8?16:12;
                    if(size<(nuint)(offset+IntPtr.Size))throw new InvalidOperationException("The clipboard metafile layout could not be preserved.");
                    metafile=CopyMetaFile(Marshal.ReadIntPtr(source,offset),null);
                    if(metafile==0)throw new Win32Exception("The clipboard metafile could not be preserved.");
                    Marshal.WriteIntPtr(destination,offset,metafile);
                }
            }
            finally{GlobalUnlock(memory);}
            var preserved=memory;memory=0;
            return new(format,preserved,h=>
            {
                if(format is 3 or 0x83)
                {
                    var data=GlobalLock(h);
                    if(data!=0){try{DeleteMetaFile(Marshal.ReadIntPtr(data,IntPtr.Size==8?16:12));}finally{GlobalUnlock(h);}}
                }
                GlobalFree(h);
            });
        }
        catch{if(metafile!=0)DeleteMetaFile(metafile);throw;}
        finally{GlobalUnlock(handle);if(memory!=0)GlobalFree(memory);}
    }

    [StructLayout(LayoutKind.Sequential)] struct NativeMessage{public nint Window;public uint Message;public nuint WParam;public nint LParam;public uint Time;public int X,Y;public uint Private;}
    [DllImport("user32.dll")] static extern int GetMessage(out NativeMessage message,nint window,uint minimum,uint maximum);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")] static extern nint DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll")] static extern nint GetClipboardOwner();
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern nint CreateWindowEx(uint extended,string className,string? name,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll",SetLastError=true)] static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll",SetLastError=true)] static extern bool EmptyClipboard();
    [DllImport("user32.dll",SetLastError=true)] static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll",SetLastError=true)] static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll",SetLastError=true)] static extern nint SetClipboardData(uint format,nint memory);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] static extern void SetLastError(uint error);
    [DllImport("kernel32.dll")] static extern nint GlobalAlloc(uint flags,nuint size);
    [DllImport("kernel32.dll")] static extern nint GlobalFree(nint memory);
    [DllImport("kernel32.dll")] static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll",EntryPoint="RtlMoveMemory")] static extern void CopyMemory(nint destination,nint source,nuint length);
    [DllImport("user32.dll",SetLastError=true)] static extern nint CopyImage(nint image,uint type,int width,int height,uint flags);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] static extern nint CopyEnhMetaFile(nint source,string? filename);
    [DllImport("gdi32.dll")] static extern bool DeleteEnhMetaFile(nint value);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] static extern nint CopyMetaFile(nint source,string? filename);
    [DllImport("gdi32.dll")] static extern bool DeleteMetaFile(nint value);
    [DllImport("gdi32.dll")] static extern uint GetPaletteEntries(nint palette,uint start,uint count,nint entries);
    [DllImport("gdi32.dll")] static extern nint CreatePalette(nint palette);
}
