using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Momiji.Interop.Wave;

namespace Momiji.Interop.AudioClient;

internal static partial class NativeMethods
{
    public enum AUDCLNT_SHAREMODE
    {
        SHARED = 0,
        EXCLUSIVE = 1,
    }

    [Flags]
    public enum AUDCLNT_STREAMFLAGS : uint
    {
        SESSIONFLAGS_EXPIREWHENUNOWNED = 0x10000000,
        SESSIONFLAGS_DISPLAY_HIDE = 0x20000000,
        SESSIONFLAGS_DISPLAY_HIDEWHENEXPIRED = 0x40000000,

        STREAMFLAGS_CROSSPROCESS = 0x00010000,
        STREAMFLAGS_LOOPBACK = 0x00020000,
        STREAMFLAGS_EVENTCALLBACK = 0x00040000,
        STREAMFLAGS_NOPERSIST = 0x00080000,
        STREAMFLAGS_RATEADJUST = 0x00100000,
        STREAMFLAGS_AUTOCONVERTPCM = 0x80000000,
        STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000
    }

    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface]
    internal partial interface IAudioClient
    {
        static Guid IID = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");

        [PreserveSig]
        int Initialize(
            AUDCLNT_SHAREMODE ShareMode,
            AUDCLNT_STREAMFLAGS StreamFlags,
            long hnsBufferDuration,
            long hnsPeriodicity,
            in WaveFormatExtensible pFormat,
            nint /*[Optional] ref Guid*/ AudioSessionGuid
        );

        [PreserveSig]
        int GetBufferSize(
            out uint pNumBufferFrames
        );

        [PreserveSig]
        int GetStreamLatency(
            out long phnsLatency
        );

        [PreserveSig]
        int GetCurrentPadding(
            out uint pNumPaddingFrames
        );

        [PreserveSig]
        int IsFormatSupported(
            AUDCLNT_SHAREMODE ShareMode,
            in WaveFormatExtensible pFormat,
            out nint ppClosestMatch
        );

        [PreserveSig]
        int GetMixFormat(
            out nint ppDeviceFormat
        );

        [PreserveSig]
        int GetDevicePeriod(
            out long phnsDefaultDevicePeriod,
            out long phnsMinimumDevicePeriod
        );

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(
            nint /*SafeWaitHandle*/ eventHandle
        );

        [PreserveSig]
        int GetService(
            in Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object? ppv
        );
    }

    public enum AUDIO_STREAM_CATEGORY
    {
        Other = 0,
        ForegroundOnlyMedia = 1,
        BackgroundCapableMedia = 2,
        Communications = 3,
        Alerts = 4,
        SoundEffects = 5,
        GameEffects = 6,
        GameMedia = 7,
        GameChat = 8,
        Speech = 9,
        Movie = 10,
        Media = 11,
        FarFieldSpeech = 12,
        UniformSpeech = 13,
        VoiceTyping = 14
    }

    [Flags]
    public enum AUDCLNT_STREAMOPTIONS
    {
        NONE = 0,
        RAW = 0x1,
        MATCH_FORMAT = 0x2,
        AMBISONICS = 0x4
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioClientProperties
    {
        public uint cbSize;
        [MarshalAs(UnmanagedType.Bool)] public bool bIsOffload;
        public AUDIO_STREAM_CATEGORY eCategory;
        public AUDCLNT_STREAMOPTIONS Options;
    }

    [Guid("726778CD-F60A-4EDA-82DE-E47610CD78AA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface]
    internal partial interface IAudioClient2 : IAudioClient
    {
        static new Guid IID = new("726778CD-F60A-4EDA-82DE-E47610CD78AA");

        [PreserveSig]
        int IsOffloadCapable(
            AUDIO_STREAM_CATEGORY Category,
            [MarshalAs(UnmanagedType.Bool)] out bool pbOffloadCapable
        );

        [PreserveSig]
        int SetClientProperties(
            nint /*ref AudioClientProperties*/ pProperties
        );

        [PreserveSig]
        int GetBufferSizeLimits(
            in WaveFormatExtensible pFormat,
            [MarshalAs(UnmanagedType.Bool)] bool bEventDriven,
            out long phnsMinBufferDuration,
            out long phnsMaxBufferDuration
        );
    }

    [Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface]
    internal partial interface IAudioClient3 : IAudioClient2
    {
        static new Guid IID = new("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42");

        [PreserveSig]
        int GetSharedModeEnginePeriod(
            in WaveFormatExtensible pFormat,
            out uint pDefaultPeriodInFrames,
            out uint pFundamentalPeriodInFrames,
            out uint pMinPeriodInFrames,
            out uint pMaxPeriodInFrames
        );

        [PreserveSig]
        int GetCurrentSharedModeEnginePeriod(
            out nint ppFormat,
            out uint pCurrentPeriodInFrames
        );

        [PreserveSig]
        int InitializeSharedAudioStream(
            AUDCLNT_STREAMFLAGS StreamFlags,
            uint PeriodInFrames,
            in WaveFormatExtensible pFormat,
            in Guid AudioSessionGuid
        );
    }

    [Flags]
    public enum AUDCLNT_BUFFERFLAGS
    {
        NONE = 0,
        DATA_DISCONTINUITY = 0x1,
        SILENT = 0x2,
        TIMESTAMP_ERROR = 0x4
    }

    [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface]
    internal partial interface IAudioRenderClient
    {
        static Guid IID = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

        [PreserveSig]
        int GetBuffer(
            uint NumFramesRequested,
            out nint ppData
        );

        [PreserveSig]
        int ReleaseBuffer(
            uint NumFramesWritten,
            AUDCLNT_BUFFERFLAGS dwFlags
        );

        [PreserveSig]
        int GetBufferSizeLimits(
            in WaveFormatExtensible pFormat,
            [MarshalAs(UnmanagedType.VariantBool)] bool bEventDriven,
            out long phnsMinBufferDuration,
            out long phnsMaxBufferDuration
        );
    }

}
