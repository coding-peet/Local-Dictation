using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LocalDictation.App.Services;

namespace LocalDictation.DeliveryProbe;

/// <summary>Exercises real native handle cloning with synthetic resources; it never publishes fixture data to the clipboard.</summary>
static class ClipboardChecks
{
    static readonly MethodInfo clone=typeof(ClipboardTransaction).GetMethod("Clone",BindingFlags.Static|BindingFlags.NonPublic)!;
    static void Require(bool passed,string detail){if(!passed)throw new InvalidOperationException(detail);}
    static (IDisposable Copy,nint Handle) Clone(uint format,nint source)
    {
        var copy=(IDisposable)clone.Invoke(null,[format,source,(nuint)0])!;
        return(copy,(nint)copy.GetType().GetField("Handle")!.GetValue(copy)!);
    }
    static nint Block(byte[] bytes)
    {
        var memory=GlobalAlloc(2,(nuint)bytes.Length);if(memory==0)throw new InvalidOperationException("Fixture allocation failed.");
        var address=GlobalLock(memory);try{Marshal.Copy(bytes,0,address,bytes.Length);}finally{GlobalUnlock(memory);}return memory;
    }
    static byte[] ReadBlock(nint memory)
    {
        var size=GlobalSize(memory);if(size>64*1024*1024)throw new InvalidOperationException("Fixture clipboard verification is limited to64MiB.");
        var bytes=new byte[checked((int)size)];var address=GlobalLock(memory);
        try{Marshal.Copy(address,bytes,0,bytes.Length);return bytes;}finally{GlobalUnlock(memory);}
    }
    public static void Run()
    {
        var sequence=GetClipboardSequenceNumber();
        foreach(var (format,bytes) in new[]{(13u,Encoding.Unicode.GetBytes("synthetic č. 42 😀\0")),
            (RegisterClipboardFormat("LocalDictationProbeBinary"),new byte[]{0,255,17,0,42,128}),
            (RegisterClipboardFormat("HTML Format"),Encoding.UTF8.GetBytes("<b>synthetic</b>\0"))})
        {
            var original=Block(bytes);var copied=Clone(format,original);
            try
            {
                var data=GlobalLock(original);try{Marshal.WriteByte(data,0,123);}finally{GlobalUnlock(original);}
                Require(ReadBlock(copied.Handle).SequenceEqual(bytes),"An independent clipboard memory clone lost its bytes.");
            }
            finally{copied.Copy.Dispose();GlobalFree(original);}
        }
        var paletteDescription=Marshal.AllocHGlobal(12);
        nint palette=0;
        try
        {
            Marshal.Copy(new byte[]{0,3,2,0,255,0,0,0,0,255,0,0},0,paletteDescription,12);
            palette=CreatePalette(paletteDescription);Require(palette!=0,"Fixture palette allocation failed.");
            var copied=Clone(9,palette);try
            {
                DeleteObject(palette);palette=0;var entries=Marshal.AllocHGlobal(8);
                try{Require(GetPaletteEntries(copied.Handle,0,2,entries)==2,"The palette clone expired with its source.");var data=new byte[8];Marshal.Copy(entries,data,0,8);Require(data.SequenceEqual(new byte[]{255,0,0,0,0,255,0,0}),"Palette entries changed.");}
                finally{Marshal.FreeHGlobal(entries);}
            }
            finally{copied.Copy.Dispose();}
        }
        finally{if(palette!=0)DeleteObject(palette);Marshal.FreeHGlobal(paletteDescription);}
        var bitmap=CreateBitmap(2,2,1,32,new byte[]{255,0,0,0,0,255,0,0,0,0,255,0,255,255,255,0});
        Require(bitmap!=0,"Fixture bitmap allocation failed.");
        try
        {
            var expected=BitmapBytes(bitmap);var copied=Clone(2,bitmap);
            try{DeleteObject(bitmap);bitmap=0;Require(BitmapBytes(copied.Handle).SequenceEqual(expected),"The bitmap clone lost pixels or depended on its source.");}
            finally{copied.Copy.Dispose();}
        }
        finally{if(bitmap!=0)DeleteObject(bitmap);}
        var dc=CreateEnhMetaFile(0,null,0,"LocalDictationProbe\0Synthetic\0\0");
        Require(dc!=0,"Fixture metafile allocation failed.");Rectangle(dc,0,0,8,8);var enhanced=CloseEnhMetaFile(dc);
        try
        {
            var expected=EnhBytes(enhanced);var copied=Clone(14,enhanced);
            try{DeleteEnhMetaFile(enhanced);enhanced=0;Require(EnhBytes(copied.Handle).SequenceEqual(expected),"The enhanced metafile clone lost its contents.");}
            finally{copied.Copy.Dispose();}
        }
        finally{if(enhanced!=0)DeleteEnhMetaFile(enhanced);}
        var source=Block([1,2,3,4]);
        try
        {
            try{var unsupported=Clone(0x200,source);unsupported.Copy.Dispose();throw new InvalidOperationException("An owner-managed clipboard format was accepted.");}
            catch(TargetInvocationException error) when(error.InnerException is InvalidOperationException){}
            Require(ReadBlock(source).SequenceEqual(new byte[]{1,2,3,4}),"Refusing a private format changed its source.");
        }
        finally{GlobalFree(source);}
        Require(GetClipboardSequenceNumber()==sequence,"Native clone checks unexpectedly changed the user's clipboard.");
    }

    public static void RecoveryChecks()
    {
        var before=Fingerprint();
        var transaction=ClipboardTransaction.Begin("Synthetic failed-restoration payload",()=>{});
        var saved=(System.Collections.IList)typeof(ClipboardTransaction).GetField("saved",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(transaction)!;
        var copyType=typeof(ClipboardTransaction).GetNestedType("FormatCopy",BindingFlags.NonPublic)!;
        var invalid=Activator.CreateInstance(copyType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
            [0u,Block([42,17,9,0]),new Action<nint>(h=>GlobalFree(h))],null)!;
        var originalCount=saved.Count;
        saved.Insert(Math.Min(1,saved.Count),invalid);
        try
        {
            // Invalid native format 0 deliberately fails AFTER a real earlier transfer when formats exist.
            transaction.Dispose();
            Require(ClipboardRecovery.HasPending,"A failed native restoration discarded its recovery backup.");
            Require(saved.Count==originalCount+1,"The complete saved format set was lost.");
            foreach(var item in saved)
                Require((nint)item!.GetType().GetField("Handle")!.GetValue(item)!!=0,"A partial restore consumed an original saved handle.");
        }
        finally
        {
            saved.Remove(invalid);((IDisposable)invalid).Dispose();
            var restored=ClipboardRecovery.RestoreAfterInspection();
            Require(restored.Succeeded,"Recovery could not retry with the complete original format set: "+restored.Message);
        }
        Require(!ClipboardRecovery.HasPending,"Successful recovery left a stale pending lease.");
        RequireSame(before,Fingerprint());
    }

    public static Dictionary<uint,string> Fingerprint()
    {
        var waiting=Stopwatch.StartNew();
        while(!OpenClipboard(0))
        {
            if(waiting.Elapsed>=TimeSpan.FromMilliseconds(400))
                throw new InvalidOperationException("The clipboard remained busy for 400 ms during fixture verification.");
            Thread.Sleep(10);
        }
        var result=new Dictionary<uint,string>();
        try
        {
            uint format=0;
            while((format=EnumClipboardFormats(format))!=0)
            {
                var handle=GetClipboardData(format);byte[] bytes;
                if(format is 2 or 0x82)bytes=BitmapBytes(handle);
                else if(format is 14 or 0x8e)bytes=EnhBytes(handle);
                else if(format==9)
                {
                    var count=GetPaletteEntries(handle,0,0,0);var data=Marshal.AllocHGlobal(checked((int)count*4));
                    try{GetPaletteEntries(handle,0,count,data);bytes=new byte[count*4];Marshal.Copy(data,bytes,0,bytes.Length);}finally{Marshal.FreeHGlobal(data);}
                }
                else if(format is 3 or 0x83)
                {
                    var memory=GlobalLock(handle);try
                    {
                        var metafile=Marshal.ReadIntPtr(memory,IntPtr.Size==8?16:12);var count=GetMetaFileBitsEx(metafile,0,null);bytes=new byte[count];GetMetaFileBitsEx(metafile,count,bytes);
                    }
                    finally{GlobalUnlock(handle);}
                }
                else bytes=ReadBlock(handle);
                result[format]=Convert.ToHexString(SHA256.HashData(bytes));
            }
            return result;
        }
        finally{CloseClipboard();}
    }
    public static void RequireSame(Dictionary<uint,string> before,Dictionary<uint,string> after)
    {
        // Windows may lazily synthesize companion text formats. Every original format must survive byte-for-byte.
        Require(before.All(item=>after.TryGetValue(item.Key,out var value)&&value==item.Value),"Delivery changed or lost an original clipboard format.");
    }
    static byte[] EnhBytes(nint value){var count=GetEnhMetaFileBits(value,0,null);Require(count>0,"Metafile data is unavailable.");var bytes=new byte[count];GetEnhMetaFileBits(value,count,bytes);return bytes;}
    static byte[] BitmapBytes(nint value)
    {
        Require(GetObject(value,Marshal.SizeOf<Bitmap>(),out var bitmap)>0,"Bitmap data is unavailable.");
        var bytes=new byte[checked(bitmap.WidthBytes*bitmap.Height)];Require(GetBitmapBits(value,bytes.Length,bytes)==bytes.Length,"Bitmap pixels are unavailable.");return bytes;
    }
    [StructLayout(LayoutKind.Sequential)] struct Bitmap{public int Type,Width,Height,WidthBytes;public ushort Planes,BitsPixel;public nint Bits;}
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] static extern nint GlobalAlloc(uint flags,nuint size);
    [DllImport("kernel32.dll")] static extern nint GlobalLock(nint value);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(nint value);
    [DllImport("kernel32.dll")] static extern nint GlobalFree(nint value);
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(nint value);
    [DllImport("gdi32.dll")] static extern nint CreatePalette(nint palette);
    [DllImport("gdi32.dll")] static extern uint GetPaletteEntries(nint palette,uint start,uint count,nint entries);
    [DllImport("gdi32.dll")] static extern nint CreateBitmap(int width,int height,uint planes,uint bitCount,byte[] bits);
    [DllImport("gdi32.dll")] static extern int GetObject(nint value,int size,out Bitmap bitmap);
    [DllImport("gdi32.dll")] static extern int GetBitmapBits(nint bitmap,int count,byte[] bits);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] static extern nint CreateEnhMetaFile(nint dc,string? file,nint rectangle,string description);
    [DllImport("gdi32.dll")] static extern nint CloseEnhMetaFile(nint dc);
    [DllImport("gdi32.dll")] static extern bool Rectangle(nint dc,int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] static extern uint GetEnhMetaFileBits(nint metafile,uint size,byte[]? data);
    [DllImport("gdi32.dll")] static extern bool DeleteEnhMetaFile(nint metafile);
    [DllImport("gdi32.dll")] static extern uint GetMetaFileBitsEx(nint metafile,uint size,byte[]? data);
}
