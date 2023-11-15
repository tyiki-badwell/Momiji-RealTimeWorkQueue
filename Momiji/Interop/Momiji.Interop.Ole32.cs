using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Momiji.Interop.Ole32;

internal static class Libraries
{
    public const string Ole32 = "Ole32.dll";
}

internal static partial class NativeMethods
{
    public enum APTTYPEQUALIFIER : int
    {
        NONE = 0,
        IMPLICIT_MTA = 1,
        NA_ON_MTA = 2,
        NA_ON_STA = 3,
        NA_ON_IMPLICIT_MTA = 4,
        NA_ON_MAINSTA = 5,
        APPLICATION_STA = 6,
        RESERVED_1 = 7
    }

    public enum APTTYPE : int
    {
        CURRENT = -1,
        STA = 0,
        MTA = 1,
        NA = 2,
        MAINSTA = 3
    }

    public enum THDTYPE : int
    {
        BLOCKMESSAGES = 0,
        PROCESSMESSAGES = 1
    }

    [LibraryImport(Libraries.Ole32, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CoGetApartmentType(
        out APTTYPE pAptType,
        out APTTYPEQUALIFIER pAptQualifier
    );

    [GeneratedComInterface]
    [Guid("000001ce-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IComThreadingInfo
    {
        [PreserveSig]
        int GetCurrentApartmentType(
            out APTTYPE pAptType
        );

        [PreserveSig]
        int GetCurrentThreadType(
            out THDTYPE pThreadType
        );

        [PreserveSig]
        int GetCurrentLogicalThreadId(
            out Guid pguidLogicalThreadId
        );

        [PreserveSig]
        int SetCurrentLogicalThreadId(
            in Guid rguid
        );
    };

    [GeneratedComInterface]
    [Guid("000001c0-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IContext
    {
        [PreserveSig]
        int SetProperty(
            in Guid rpolicyId,
            int flags, //need 0
            [MarshalAs(UnmanagedType.Interface)] object pUnk
        );
        
        [PreserveSig]
        int RemoveProperty(
            in Guid rPolicyId
        );
        
        [PreserveSig]
        int GetProperty(
            in Guid rGuid,
            out int pFlags,
            [MarshalAs(UnmanagedType.Interface)] out object ppUnk
        );
        
        [PreserveSig]
        int EnumContextProps(
            [MarshalAs(UnmanagedType.Interface)] out object /*IEnumContextProps*/ ppEnumContextProps
        );
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
    internal struct ContextProperty
    {
        public Guid policyId;
        public uint flags;
        public nint pUnk;
    }

    [GeneratedComInterface]
    [Guid("000001c1-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IEnumContextProps
    {
        [PreserveSig]
        int Next(
            uint celt,
            out ContextProperty pContextProperties,
            out uint pceltFetched
        );

        [PreserveSig]
        int Skip(
            uint celt
        );
        
        [PreserveSig]
        int Reset();
        
        [PreserveSig]
        int Clone(
            [MarshalAs(UnmanagedType.Interface)] out object ppEnumContextProps
        );
        
        [PreserveSig]
        int Count(
            out uint pcelt
        );
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
    internal struct ComCallData
    {
        public uint dwDispid;
        public uint dwReserved;
        public nint pUserDefined;
    }

    [GeneratedComInterface]
    [Guid("000001da-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IContextCallback
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ContextCall(
            in ComCallData pParam
        );
        
        [PreserveSig]
        int ContextCallback(
            nint /*ContextCall*/ pfnCallback,
            in ComCallData pParam,
            in Guid riid,
            int iMethod,
            [MarshalAs(UnmanagedType.Interface)] object pUnk
        );
    };

    [LibraryImport(Libraries.Ole32, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CoGetObjectContext(
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv
    );

    [GeneratedComInterface]
    [Guid("C03F6A43-65A4-9818-987E-E0B810D2A6F2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IAgileReference
    {
        [PreserveSig]
        int Resolve(
            in Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppvObjectReference
        );
    }

    internal enum AgileReferenceOptions
    {
        DEFAULT = 0,
        DELAYEDMARSHAL = 1,
    };

    [LibraryImport(Libraries.Ole32, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RoGetAgileReference(
        AgileReferenceOptions options,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] object pUnk,
        out IAgileReference ppAgileReference
    );

    [LibraryImport(Libraries.Ole32, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CoLockObjectExternal(
        [MarshalAs(UnmanagedType.Interface)] object pUnk,
        [MarshalAs(UnmanagedType.Bool)] bool fLock,
        [MarshalAs(UnmanagedType.Bool)] bool fLastUnlockReleases
    );

}
