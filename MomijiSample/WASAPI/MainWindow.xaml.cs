using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momiji.Core.RTWorkQueue;
using Momiji.Core.Threading;
using Momiji.Interop.Wave;
using Windows.Devices.Enumeration;
using AudioClient = Momiji.Interop.AudioClient.NativeMethods;
using MMDeviceApi = Momiji.Interop.MMDeviceApi.NativeMethods;

namespace Momiji.Sample.WASAPI;

public sealed partial class MainWindow : Window
{
    public ViewModel ViewModel { get; }
    private readonly ILogger _logger;

    public MainWindow(
        ILogger<MainWindow> logger,
        ViewModel viewModel
    )
    {
        _logger = logger;
        ViewModel = viewModel;

        InitializeComponent();
        _logger.LogInformation("Created");
    }
}

public partial class Model : INotifyPropertyChanged
{
    private readonly ILogger _logger;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DeviceInformation> DeviceInformationList { get; } = [];
    private string? _selectedDeviceInformationText;
    public string? SelectedDeviceInformationText
    {
        get => _selectedDeviceInformationText;
        set
        {
            _selectedDeviceInformationText = value;
            OnPropertyChanged();
        }
    }

    public void OnPropertyChanged([CallerMemberName] string? propertyName = default)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }


    public Model(
        ILogger<Model> logger
    )
    {
        _logger = logger;
        _logger.LogInformation("Created");
    }
}

public class ViewModel
{
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly ILogger _logger;
    private readonly WASAPIController _wasapiController;

    public Model Model { get; }

    public ViewModel(
        ILogger<ViewModel> logger,
        IHostApplicationLifetime hostApplicationLifetime,
        WASAPIController wasapiController,
        Model model
    )
    {
        _logger = logger;
        _hostApplicationLifetime = hostApplicationLifetime;
        _wasapiController = wasapiController;
        Model = model;
        _logger.LogInformation("Created");
    }

    public void Closed()
    {
        _logger.LogInformation("Closed");
        _hostApplicationLifetime.StopApplication();
    }

    private static readonly JsonSerializerOptions s_options = new() {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private DeviceInformation? _deviceInformation;

    public void SelectionChanged(
        object sender,
        SelectionChangedEventArgs _
    )
    {
        _logger.LogInformation("SelectionChanged");
        var listBox = sender as ListBox;
        _deviceInformation = listBox?.SelectedItem as DeviceInformation;

        Model.SelectedDeviceInformationText = JsonSerializer.Serialize(_deviceInformation, s_options);
    }

    public async void Play()
    {
        _logger.LogInformation("Play");
        await _wasapiController.PlayAsync(_deviceInformation!.Id);
    }

    public void Stop()
    {
        _logger.LogInformation("Stop");
        _wasapiController.Stop();
    }
}

public partial class WASAPIController : IDisposable
{
    private readonly IRTWorkQueuePlatformEventsHandler _RTWorkQueuePlatformEventsHandler;
    private readonly IRTWorkQueueManager _RTWorkQueueManager;
    private readonly ILogger _logger;
    private bool _disposed;
    public Model Model { get; }

    private readonly DeviceWatcher _deviceWatcher;
    private readonly Dictionary<string, DeviceInformation> _deviceInformationMap = [];

    private CancellationTokenSource? _cts = default;

    public WASAPIController(
        ILogger<WASAPIController> logger,
        IRTWorkQueuePlatformEventsHandler rtWorkQueuePlatformEventsHandler,
        IRTWorkQueueManager rtWorkQueueManager,
        Model model
    )
    {
        _logger = logger;
        _RTWorkQueuePlatformEventsHandler = rtWorkQueuePlatformEventsHandler;
        _RTWorkQueueManager = rtWorkQueueManager;
        
        Model = model;

        _deviceWatcher = DeviceInformation.CreateWatcher(DeviceClass.AudioRender);
        _deviceWatcher.Added += (_, i) => {
            _deviceInformationMap.Add(i.Id, i);

            Model.DeviceInformationList.Add(i);
            _logger.LogInformation($"""
                Added
                name:{i.Name}
                id:{i.Id}
                in lid:{i.EnclosureLocation.InLid} 
                in dock:{i.EnclosureLocation.InDock}
                panel:{i.EnclosureLocation.Panel}
                RotationAngleInDegreesClockwise:{i.EnclosureLocation.RotationAngleInDegreesClockwise}
                default:{i.IsDefault}
                enabled:{i.IsEnabled}
                kind:{i.Kind}
                ProtectionLevel:{i.Pairing.ProtectionLevel}
                CanPair:{i.Pairing.CanPair}
                Custom:{i.Pairing.Custom}
                IsPaired:{i.Pairing.IsPaired}
                """);
        };
        _deviceWatcher.Removed += (_, i) => {
            if (_deviceInformationMap.TryGetValue(i.Id, out var item))
            {
                _deviceInformationMap.Remove(i.Id);
                Model.DeviceInformationList.Remove(item);
                _logger.LogInformation($"""
                Remove
                id:{i.Id}
                """);
            }
        };
        _deviceWatcher.Updated += (_, i) => {
            if (_deviceInformationMap.TryGetValue(i.Id, out var item))
            {
                var index = Model.DeviceInformationList.IndexOf(item);
                item.Update(i);
                Model.DeviceInformationList[index] = item;
                _logger.LogInformation($"""
                Updated
                id:{i.Id}
                """);
            }
        };
        _deviceWatcher.Start();

        _logger.LogInformation("Created");
    }

    ~WASAPIController()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
        }

        _deviceWatcher.Stop();

        _disposed = true;
        _logger.LogInformation("Dispose");
    }

    public async Task PlayAsync(
        string deviceInterfacePath
    )
    {
        if (_cts != default)
        {
            _logger.LogInformation("already started.");
            return;
        }

        _cts = new();

        var audioClient = await ActivateAsync<AudioClient.IAudioClient3>(deviceInterfacePath, AudioClient.IAudioClient3.IID);

        using var out_ = new WASAPIOut(_RTWorkQueueManager, audioClient, _logger);
        
        out_.Initialize(false, false, false);

        var audioWaveTheta = 0.0;
        out_.Start((ptr, frames) => {
            //_logger.LogInformation($"process {ptr:X} {frames} {(double)counter.ElapsedTicks / 10000}");
            unsafe
            {
                var data = new Span<float>((void*)ptr, (int)frames * 2);

                var freq = 0.480f; // choosing to generate frequency of 1kHz
                var amplitude = 0.3f;
                var sampleIncrement = (freq * (Math.PI * 2)) / 48000;
                // Generate a 1kHz sine wave and populate the values in the memory buffer
                var idx = 0;
                for (var i = 0; i < frames; i++)
                {
                    var sinValue = amplitude * Math.Sin(audioWaveTheta);
                    data[idx++] = (float)sinValue; //L
                    data[idx++] = (float)sinValue; //R
                    audioWaveTheta += sampleIncrement;
                }
            }

            return frames;
        }, _cts.Token);

        while (!_cts.IsCancellationRequested)
        {
            await Task.Delay(500);
        }

        _logger.LogInformation("canceled.");

        _cts.Dispose();
        _cts = default;
    }

    public void Stop()
    {
        if (_cts == default)
        {
            _logger.LogInformation("already stopped.");
            return;
        }

        _cts.Cancel();
    }

    [ClassInterface(ClassInterfaceType.None)]
    [GeneratedComClass]
    private partial class ActivateAudioInterfaceCompletionHandler : MMDeviceApi.IActivateAudioInterfaceCompletionHandler, MMDeviceApi.IAgileObject
    {
        private readonly Action<MMDeviceApi.IActivateAudioInterfaceAsyncOperation> _action;

        public ActivateAudioInterfaceCompletionHandler(
            Action<MMDeviceApi.IActivateAudioInterfaceAsyncOperation> action
        )
        {
            _action = action;
        }

        public int ActivateCompleted(MMDeviceApi.IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            _action(activateOperation);
            return 0;
        }
    }

    private async Task<T> ActivateAsync<T>(
        string deviceInterfacePath,
        Guid iid
    ) where T : notnull, AudioClient.IAudioClient
    {
        _logger.LogInformation($"ActivateAsync {ApartmentType.GetApartmentType()}");

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.AttachedToParent);

        var h = new ActivateAudioInterfaceCompletionHandler((activateOperation) => {
            _logger.LogInformation("ActivateCompleted");
            try
            {
                Marshal.ThrowExceptionForHR(activateOperation.GetActivateResult(out var activateResult, out var activatedInterface));
                Marshal.ThrowExceptionForHR(activateResult);

                tcs.SetResult((T)activatedInterface!);
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        Marshal.ThrowExceptionForHR(MMDeviceApi.ActivateAudioInterfaceAsync(
            deviceInterfacePath,
            iid,
            nint.Zero, // activationParams ÇÕIAudioClientÇ≈ÇÕégÇÌÇ»Ç¢
            h,
            out var activationOperation
        ));

        return await tcs.Task;
    }

}

public partial class WASAPIOut : IDisposable
{
    private readonly ILogger _logger;

    private bool _disposed;

    private bool _exclusive;

    private WaveFormatExtensible _format;

    private readonly AudioClient.IAudioClient3 _audioClient;
    private AudioClient.IAudioRenderClient? _audioRenderClient;


    private readonly IRTWorkQueueManager _workQueueManager;
    private readonly EventWaitHandle _eventWaitHandle;

    internal WASAPIOut(
        IRTWorkQueueManager workQueueManager,
        AudioClient.IAudioClient3 audioClient,
        ILogger logger
    )
    {
        _logger = logger;
        _audioClient = audioClient;
        _workQueueManager = workQueueManager;
        _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
    }

    ~WASAPIOut()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
        }

        if (_audioRenderClient != default)
        {
            //var count = Marshal.FinalReleaseComObject(_audioRenderClient);
            _audioRenderClient = null;
            //_logger.LogInformation($"_audioRenderClient FinalReleaseComObject {count}");
        }

        if (_audioClient != default)
        {
            //var count = Marshal.FinalReleaseComObject(_audioClient);
            //_audioClient = null;
            //_logger.LogInformation($"_audioClient FinalReleaseComObject {count}");
        }

        _eventWaitHandle.Dispose();

        _disposed = true;
        _logger.LogInformation("Dispose");
    }

    private WaveFormatExtensible MakeFormatSupported(
        AudioClient.AUDCLNT_SHAREMODE shareMode,
        ushort channels = 2,
        uint samplesPerSecond = 48000
    )
    {
        var format = new WaveFormatExtensible();
        format.wfe.formatType = WaveFormatEx.FORMAT.EXTENSIBLE;
        format.wfe.channels = channels;
        format.wfe.samplesPerSecond = samplesPerSecond;
        format.wfe.bitsPerSample = (ushort)(Marshal.SizeOf<float>() * 8);
        format.wfe.blockAlign = (ushort)(format.wfe.channels * format.wfe.bitsPerSample / 8);
        format.wfe.averageBytesPerSecond = format.wfe.samplesPerSecond * format.wfe.blockAlign;
        format.wfe.size = (ushort)(Marshal.SizeOf<WaveFormatExtensiblePart>());

        format.exp.validBitsPerSample = format.wfe.bitsPerSample;
        format.exp.channelMask = WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT;
        format.exp.subFormat = new Guid("00000003-0000-0010-8000-00aa00389b71"); //MEDIASUBTYPE_IEEE_FLOAT

        _logger.LogInformation($"""
                format:{format}
                """);

        {
            Marshal.ThrowExceptionForHR(_audioClient!.GetMixFormat(out var ptr));

            var format2 = Marshal.PtrToStructure<WaveFormatExtensible>(ptr);
            Marshal.FreeCoTaskMem(ptr);

            _logger.LogInformation($"""
                    GetMixFormat:{format2}
                    """);
        }

        {
            Marshal.ThrowExceptionForHR(_audioClient!.IsFormatSupported(
                shareMode,
                format,
                out var ptr
            ));

            if (ptr != nint.Zero) //S_FALSE Ç™ï‘Ç¡ÇƒÇ´ÇƒÇÈëOíÒ
            {
                var format2 = Marshal.PtrToStructure<WaveFormatExtensible>(ptr);
                Marshal.FreeCoTaskMem(ptr);

                _logger.LogInformation($"""
                    IsFormatSupported S_FALSE:{format2}
                    """);

                format = format2;
            }
        }

        return format;
    }

    private T GetService<T>(Guid iid)
    {
        Marshal.ThrowExceptionForHR(_audioClient!.GetService(
            iid,
            out var o
        ));

        return (T)o!;
    }


    public void Initialize(
        bool exclusive,
        bool lowLatency,
        bool offload
    )
    {
        _exclusive = exclusive;

        var sharemode = _exclusive ? AudioClient.AUDCLNT_SHAREMODE.EXCLUSIVE : AudioClient.AUDCLNT_SHAREMODE.SHARED;

        foreach (var category in Enum.GetValues<AudioClient.AUDIO_STREAM_CATEGORY>())
        {
            var result = _audioClient!.IsOffloadCapable(category, out var pbOffloadCapable);
            _logger.LogInformation($"""
                category:{category}
                pbOffloadCapable:{pbOffloadCapable}
                error:{Marshal.GetPInvokeErrorMessage(result)}
                """);
        }

        /*
        {
            var audioClientProperties = new AudioClient.AudioClientProperties
            {
                cbSize = (uint)Marshal.SizeOf<AudioClient.AudioClientProperties>(),
                bIsOffload = offload,
                eCategory = AudioClient.AUDIO_STREAM_CATEGORY.Media,
                Options = AudioClient.AUDCLNT_STREAMOPTIONS.MATCH_FORMAT
            };

            var result = _audioClient!.SetClientProperties(ref audioClientProperties);
            _logger.LogInformation($"SetClientProperties:{Marshal.GetPInvokeErrorMessage(result)}");
        }
        */

        _format = MakeFormatSupported(sharemode);

        if (lowLatency)
        {
            Marshal.ThrowExceptionForHR(_audioClient!.GetSharedModeEnginePeriod(
                _format, 
                out var pDefaultPeriodInFrames, 
                out var pFundamentalPeriodInFrames, 
                out var pMinPeriodInFrames, 
                out var pMaxPeriodInFrames
            ));

            _logger.LogInformation($"""
                pDefaultPeriodInFrames:{pDefaultPeriodInFrames}
                pFundamentalPeriodInFrames:{pFundamentalPeriodInFrames}
                pMinPeriodInFrames:{pMinPeriodInFrames}
                pMaxPeriodInFrames:{pMaxPeriodInFrames}
                """);

            Marshal.ThrowExceptionForHR(_audioClient!.InitializeSharedAudioStream(
                AudioClient.AUDCLNT_STREAMFLAGS.STREAMFLAGS_EVENTCALLBACK
                | AudioClient.AUDCLNT_STREAMFLAGS.STREAMFLAGS_NOPERSIST,
                pMinPeriodInFrames,
                _format,
                Guid.Empty
            ));
        }
        else
        {
            Marshal.ThrowExceptionForHR(_audioClient!.Initialize(
                sharemode,
                AudioClient.AUDCLNT_STREAMFLAGS.STREAMFLAGS_EVENTCALLBACK
                | AudioClient.AUDCLNT_STREAMFLAGS.STREAMFLAGS_NOPERSIST,
                30 * 10000,
                0,
                _format,
                nint.Zero
            ));
        }


        Marshal.ThrowExceptionForHR(_audioClient.SetEventHandle(_eventWaitHandle.SafeWaitHandle.DangerousGetHandle()));

        _audioRenderClient = GetService<AudioClient.IAudioRenderClient>(AudioClient.IAudioRenderClient.IID);


        Marshal.ThrowExceptionForHR(_audioClient.GetBufferSize(out var pNumBufferFrames));
        Marshal.ThrowExceptionForHR(_audioClient.GetCurrentPadding(out var pNumPaddingFrames));
        Marshal.ThrowExceptionForHR(_audioClient.GetDevicePeriod(out var phnsDefaultDevicePeriod, out var phnsMinimumDevicePeriod));
        Marshal.ThrowExceptionForHR(_audioClient.GetStreamLatency(out var pNumStreamLatency));

        _logger.LogInformation($"""
                pNumBufferFrames:{pNumBufferFrames}
                pNumPaddingFrames:{pNumPaddingFrames}
                phnsDefaultDevicePeriod:{phnsDefaultDevicePeriod}
                phnsMinimumDevicePeriod:{phnsMinimumDevicePeriod}
                pNumStreamLatency:{pNumStreamLatency}
                """);

        Marshal.ThrowExceptionForHR(_audioClient.GetMixFormat(out var ptr));
        if (ptr != nint.Zero)
        {
            _format = Marshal.PtrToStructure<WaveFormatExtensible>(ptr);
            Marshal.FreeCoTaskMem(ptr);

            _logger.LogInformation($"""
                GetMixFormat:{_format}
                """);
        }

        if (offload)
        {
            /*Marshal.ThrowExceptionForHR*/
            var result = (_audioClient.GetBufferSizeLimits(
                _format, 
                true, 
                out var phnsMinBufferDuration, 
                out var phnsMaxBufferDuration
            ));
            _logger.LogInformation($"""
                phnsMinBufferDuration:{phnsMinBufferDuration}
                phnsMaxBufferDuration:{phnsMaxBufferDuration}
                error:{Marshal.GetPInvokeErrorMessage(result)}
                """);
        }
    }

    public void Start(
        Func<nint, uint, uint> process,
        CancellationToken ct = default
    )
    {
        Action? put = null;

        void action()
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogInformation("canceled.");
                return;
            }
            Process(process);
            put!();
        }

        put = () =>
        {
            _workQueueManager.PutWaitingWorkItem(
                0,
                _eventWaitHandle,
                action,
                null,
                ct
            );
        };

        put();

        Marshal.ThrowExceptionForHR(_audioClient!.Start());
    }

    public void Stop()
    {
        //TODO cancel
        Marshal.ThrowExceptionForHR(_audioClient!.Stop());
    }

    public void Reset()
    {
        Marshal.ThrowExceptionForHR(_audioClient!.Reset());
    }

    private void Process(Func<nint, uint, uint> func)
    {
        Marshal.ThrowExceptionForHR(_audioClient!.GetBufferSize(out var pNumBufferFrames));
        Marshal.ThrowExceptionForHR(_audioClient.GetCurrentPadding(out var pNumPaddingFrames));

        var numFramesRequested = pNumBufferFrames - pNumPaddingFrames;

        //_logger.LogTrace($"GetBuffer:{numFramesRequested} (pNumBufferFrames:{pNumBufferFrames} pNumPaddingFrames:{pNumPaddingFrames})");
        Marshal.ThrowExceptionForHR(_audioRenderClient!.GetBuffer(
            numFramesRequested,
            out var ppData
        ));

        if (ppData == nint.Zero)
        {
            return;
        }

        var written = func(ppData, numFramesRequested);

        //TODO blockAlignÇ≈äÑÇÁÇ»Ç¢Ç∆ìÆçÏÇµÇ»Ç¢ÅH
        var numFramesWritten = written / _format.wfe.blockAlign;

        //_logger.LogTrace($"ReleaseBuffer:{numFramesWritten}");
        Marshal.ThrowExceptionForHR(_audioRenderClient.ReleaseBuffer(
            numFramesWritten,
            AudioClient.AUDCLNT_BUFFERFLAGS.NONE
        ));
    }

}
