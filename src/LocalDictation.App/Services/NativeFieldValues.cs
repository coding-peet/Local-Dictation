using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LocalDictation.App.Services;

// Read-only COM prefix interfaces verified against Windows SDK 10.0.22621.0
// UIAutomationClient.idl. This exposes properties absent from the .NET wrapper.
static class NativeFieldValues
{
    internal sealed record Shape(bool Available,bool? ReadOnly,int? Length,string? Kind,bool? EqualsDocument);
    internal sealed record Snapshot(string Status,Shape? Value,Shape? Legacy,int? LegacyRole,uint? LegacyState);
    [ComImport,Guid("ff48dba4-60ef-4201-aa87-54103eef594e")] sealed class Client{}
    [StructLayout(LayoutKind.Sequential)] struct Point{public int X,Y;}
    [ComImport,Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IClient
    {
        [PreserveSig] int CompareElements(nint a,nint b,[MarshalAs(UnmanagedType.Bool)]out bool same);
        [PreserveSig] int CompareRuntimeIds(nint a,nint b,[MarshalAs(UnmanagedType.Bool)]out bool same);
        [PreserveSig] int GetRootElement(out nint root);
        [PreserveSig] int ElementFromHandle(nint hwnd,out nint element);
        [PreserveSig] int ElementFromPoint(Point point,out nint element);
        [PreserveSig] int GetFocusedElement([MarshalAs(UnmanagedType.Interface)]out IElement element);
    }
    [ComImport,Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IElement
    {
        [PreserveSig] int SetFocus();
        [PreserveSig] int GetRuntimeId([MarshalAs(UnmanagedType.SafeArray,SafeArraySubType=VarEnum.VT_I4)]out int[] identity);
        [PreserveSig] int FindFirst(int scope,nint condition,out nint found);
        [PreserveSig] int FindAll(int scope,nint condition,out nint found);
        [PreserveSig] int FindFirstBuildCache(int scope,nint condition,nint cache,out nint found);
        [PreserveSig] int FindAllBuildCache(int scope,nint condition,nint cache,out nint found);
        [PreserveSig] int BuildUpdatedCache(nint cache,out nint updated);
        [PreserveSig] int GetCurrentPropertyValue(int property,[MarshalAs(UnmanagedType.Struct)]out object value);
        [PreserveSig] int GetCurrentPropertyValueEx(int property,[MarshalAs(UnmanagedType.Bool)]bool noDefault,[MarshalAs(UnmanagedType.Struct)]out object value);
    }
    static string? Kind(string? text)=>text switch{null=>null,""=>"Empty","\n"=>"LF","\r\n"=>"CRLF",_=>text.All(char.IsWhiteSpace)?"Whitespace":"Other"};
    internal static Shape Describe(bool available,bool? readOnly,string? value,string document)=>
        new(available,readOnly,value?.Length,Kind(value),value==null?null:value==document);
    static object Property(IElement element,int property)
    {Marshal.ThrowExceptionForHR(element.GetCurrentPropertyValueEx(property,true,out var value));return value;}
    internal static Snapshot Read(AutomationElement expected,string document)
    {
        IClient? client=null;IElement? element=null;
        try
        {
            client=(IClient)(object)new Client();
            Marshal.ThrowExceptionForHR(client.GetFocusedElement(out element));
            Marshal.ThrowExceptionForHR(element.GetRuntimeId(out var identity));
            if(!identity.SequenceEqual(expected.GetRuntimeId())||Property(element,30002) is not int pid||pid!=expected.Current.ProcessId)
                return new("different-focus",null,null,null,null);
            var valueAvailable=Property(element,30043) is true;
            var value=valueAvailable?Property(element,30045) as string:null;
            bool? readOnly=valueAvailable&&Property(element,30046) is bool ro?ro:null;
            var legacyAvailable=Property(element,30090) is true;
            var legacyValue=legacyAvailable?Property(element,30093) as string:null;
            int? role=null;uint? state=null;
            if(legacyAvailable)
            {
                var roleObject=Property(element,30095);var stateObject=Property(element,30096);
                if(roleObject is int r)role=r;else if(roleObject is uint ur)role=checked((int)ur);
                if(stateObject is int st)state=unchecked((uint)st);else if(stateObject is uint ust)state=ust;
            }
            return new("matched",Describe(valueAvailable,readOnly,value,document),
                Describe(legacyAvailable,state.HasValue?(state.Value&0x40)!=0:null,legacyValue,document),role,state);
        }
        finally
        {
            if(element!=null)Marshal.ReleaseComObject(element);
            if(client!=null)Marshal.ReleaseComObject(client);
        }
    }
}
