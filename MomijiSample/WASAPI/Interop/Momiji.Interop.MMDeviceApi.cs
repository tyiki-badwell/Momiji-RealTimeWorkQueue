using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Momiji.Interop.MMDeviceApi;
internal static class Libraries
{
    public const string Mmdevapi = "Mmdevapi.dll";
}

internal static partial class NativeMethods
{

    [LibraryImport(Libraries.Mmdevapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        in Guid riid,
        nint /*PROPVARIANT*/ activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation
    );

    [Guid("41d949ab-9862-444a-80f6-c261334da5eb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface]
    internal partial interface IActivateAudioInterfaceCompletionHandler
    {
        static Guid IID = new("41d949ab-9862-444a-80f6-c261334da5eb");

        [PreserveSig]
        int ActivateCompleted(
            IActivateAudioInterfaceAsyncOperation activateOperation
        );
    }

    [Guid("72a22d78-cde4-431d-b8cc-843a71199b6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface]
    internal partial interface IActivateAudioInterfaceAsyncOperation
    {
        static Guid IID = new("72a22d78-cde4-431d-b8cc-843a71199b6d");

        [PreserveSig]
        int GetActivateResult(
            out int activateResult,
            [MarshalAs(UnmanagedType.Interface)] out object? activatedInterface
        );
    }

    [Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface]
    internal partial interface IAgileObject
    {
        static Guid IID = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");
    }

    //Windows.Devices.Enumeration対照表
    /*



     IMMDevice 
         Activate(
            _In_ REFIID iid,
            _In_ DWORD dwClsCtx,
            _In_opt_ PROPVARIANT *pActivationParams,
            _Out_  void** ppInterface) = 0;

        OpenPropertyStore(
            _In_ DWORD stgmAccess,
            _Out_ IPropertyStore **ppProperties) = 0;

        GetId(
            _Outptr_ LPWSTR *ppstrId) = 0;

        GetState(
            _Out_ DWORD *pdwState


    IMMDeviceCollection 
        GetCount(
            _Out_ UINT *pcDevices) = 0;

        Item(
            _In_ UINT nDevice,
            _Out_ IMMDevice **ppDevice) = 0;

    IMMDeviceEnumerator      
        EnumAudioEndpoints( 
            _In_  EDataFlow dataFlow,
            _In_  DWORD dwStateMask,
            _Out_  IMMDeviceCollection **ppDevices) = 0;

        GetDefaultAudioEndpoint( 
            _In_  EDataFlow dataFlow,
            _In_  ERole role,
            _Out_  IMMDevice **ppEndpoint) = 0;

        GetDevice( 
            _In_  LPCWSTR pwstrId,
            _Out_  IMMDevice **ppDevice) = 0;

        RegisterEndpointNotificationCallback( 
            _In_  IMMNotificationClient *pClient) = 0;

        UnregisterEndpointNotificationCallback( 
            _In_  IMMNotificationClient *pClient) = 0;


    IMMEndpoint
         GetDataFlow( 
                _Out_  EDataFlow *pDataFlow) = 0;

     */




    internal enum EDataFlow : uint
    {
        eRender,
        eCapture,
        eAll,
        EDataFlow_enum_count
    };

    internal enum ERole : uint
    {
        eConsole,
        eMultimedia,
        eCommunications,
        ERole_enum_count
    };

    internal enum EndpointFormFactor : uint
    {
        RemoteNetworkDevice,
        Speakers,
        LineLevel,
        Headphones,
        Microphone,
        Headset,
        Handset,
        UnknownDigitalPassthrough,
        SPDIF,
        DigitalAudioDisplayDevice,
        UnknownFormFactor,
        EndpointFormFactor_enum_count
    };

    [Guid("7991eec9-7e89-4d85-8390-6c703cec60c0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface]
    internal partial interface IMMNotificationClient
    {
        [PreserveSig]
        int OnDeviceStateChanged(
            [MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId,
            uint dwNewState
        );

        [PreserveSig]
        int OnDeviceAdded(
            [MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId
        );

        [PreserveSig]
        int OnDeviceRemoved(
            [MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId
        );

        [PreserveSig]
        int OnDefaultDeviceChanged(
            EDataFlow flow,
            ERole role,
            [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId
        );

        [PreserveSig]
        int OnPropertyValueChanged(
            [MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId,
            nint/*PROPERTYKEY*/ key
        );
    }

}
