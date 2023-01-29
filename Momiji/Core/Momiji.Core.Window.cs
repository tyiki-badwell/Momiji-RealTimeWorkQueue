﻿using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

public class WindowException : Exception
{
    public WindowException(string message) : base(message)
    {
    }

    public WindowException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

internal class WindowSecurity : ObjectSecurity<User32.DESKTOP_ACCESS_MASK>
{
    public WindowSecurity()
        : base(
              false,
              ResourceType.WindowObject,
              (SafeHandle?)null,
              AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access //| AccessControlSections.Audit
          )
    {

    }
    public WindowSecurity(SafeHandle handle)
        : base(
              false,
              ResourceType.WindowObject,
              handle,
              AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access //| AccessControlSections.Audit
          )
    {

    }

    public new void Persist(SafeHandle handle)
    {
        Persist(
            handle,
            AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access //| AccessControlSections.Audit
        );
    }
}

public interface IWindowManager : IDisposable
{
    Task StartAsync(CancellationToken stoppingToken);
    void Cancel();

    public delegate void OnPreCloseWindow();
    public delegate void OnPostPaint(nint hWindow);

    public IWindow CreateWindow(
        OnPreCloseWindow? onPreCloseWindow = default,
        OnPostPaint? onPostPaint = default
    );

    void CloseAll();
}

public interface IWindow
{
    nint Handle
    {
        get;
    }
    T Dispatch<T>(Func<T> item);
    bool Close();
    bool Move(
        int x,
        int y,
        int width,
        int height,
        bool repaint
    );

    bool Show(
        int cmdShow
    );

    void SetWindowStyle(
        int style
    );
}


public class WindowManager : IDisposable, IWindowManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly object _sync = new();
    private CancellationTokenSource? _processCancel;
    private Task? _processTask;
    private int _uiThreadId;

    private readonly PinnedDelegate<User32.WNDPROC> _wndProc;
    private readonly WindowClass _windowClass;

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _queueEvent = new();

    private readonly ConcurrentDictionary<nint, NativeWindow> _windowMap = new();
    private readonly ConcurrentDictionary<nint, NativeWindow> _windowHashMap = new();

    public WindowManager(
//        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
//        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        //TODO windowとthreadが1:1のモード
//        _configurationSection = configuration.GetSection($"{typeof(WindowManager).FullName}");

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowManager>();

        _wndProc = new PinnedDelegate<User32.WNDPROC>(new(WndProc));
        _windowClass =
            new WindowClass(
                _loggerFactory,
                _wndProc,
                User32.WNDCLASS.CS.OWNDC
            );
    }
    ~WindowManager()
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
            _logger.LogInformation("disposing");
            try
            {
                Cancel();
            }
            finally
            {
//                _desktop?.Close();
//                _windowStation?.Close();

                _windowClass.Dispose();
                _wndProc.Dispose();
            }
        }

        _disposed = true;
    }

    public void Cancel()
    {
        var processCancel = _processCancel;
        if (processCancel == null)
        {
            _logger.LogInformation("already stopped.");
            return;
        }

        var task = _processTask;
        try
        {
            processCancel.Cancel();
            task?.Wait();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "failed.");
        }
    }

    internal T Dispatch<T>(Func<T> item)
    {
        if (_uiThreadId == Environment.CurrentManagedThreadId)
        {
            _logger.LogTrace("Dispatch called from same thread id then immidiate mode");
            return item.Invoke();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.AttachedToParent);
        Dispatch(() => {
            try
            {
                var result = item.Invoke();
                tcs.SetResult(result);
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        if (!tcs.Task.Wait(5000, CancellationToken.None))
        {
            _logger.LogError("Dispatch timeout");
        }
        return tcs.Task.Result;
    }


    private void Dispatch(Action item)
    {
        _logger.LogWithThreadId(LogLevel.Information, "Dispatch", Environment.CurrentManagedThreadId);
        if (_processCancel == default)
        {
            throw new WindowException("message loop is not exists.");
        }

        _queue.Enqueue(item);
        _queueEvent.Set();
    }

    public IWindow CreateWindow(
        IWindowManager.OnPreCloseWindow? onPreCloseWindow = default,
        IWindowManager.OnPostPaint? onPostPaint = default
    )
    {
        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                onPreCloseWindow,
                onPostPaint
            );

        _windowHashMap.TryAdd(window.GetHashCode(), window);

        window.CreateWindow(_windowClass);

        return window;
    }

    public void CloseAll()
    {
        foreach (var window in _windowMap.Values)
        {
            try
            {
                window.Close();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "close failed.");
            }
        }

/*
        _windowMap.Values.AsParallel().ForAll(window => {
            try
            {
                window.Close();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "close failed.");
            }
        });
*/
    }

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        lock (_sync)
        {
            if (_processCancel != null)
            {
                _logger.LogInformation("already started.");
                return;
            }
            _processCancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        }

        _processTask = Run();

        try
        {
            await _processTask.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "process task failed.");
        }

        _logger.LogWithThreadId(LogLevel.Information, "process task end", Environment.CurrentManagedThreadId);

        Cancel();
        _logger.LogTrace("cancel end");

        _processTask = default;

        _processCancel?.Dispose();
        _processCancel = default;

        _logger.LogInformation("stopped.");
    }

    private Task Run()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
        var thread = new Thread(() =>
        {
            _logger.LogInformation("thread start");
            Exception? error = default; 
            try
            {
                WindowDebug.CheckIntegrityLevel(_loggerFactory);
                WindowDebug.CheckDesktop(_loggerFactory);
                WindowDebug.CheckGetProcessInformation(_loggerFactory);

                MessageLoop();
                _logger.LogWithThreadId(LogLevel.Information, "message loop normal end", Environment.CurrentManagedThreadId);
            }
            catch (Exception e)
            {
                error = e;
                _logger.LogError(e, "message loop exception");
                _processCancel?.Cancel();
            }

            try
            {
                if (!_windowMap.IsEmpty)
                {
                    //クローズできていないwindowが残っているのは異常事態
                    _logger.LogWarning($"window left {_windowMap.Count}");

                    foreach (var item in _windowMap)
                    {
                        //TODO closeした通知を流す必要あり

                        _logger.LogWarning($"DestroyWindow {item.Key:X}");
                        User32.DestroyWindow(item.Key);
                    }

                    _windowMap.Clear();
                }
            }
            catch (Exception e)
            {
                error = e;
                _logger.LogError(e, "window clean up exception");
            }

            if (error != default)
            {
                tcs.SetException(new WindowException("message loop exception", error));
            }
            else
            {
                tcs.SetResult();
            }

            _uiThreadId = default;
        })
        {
            IsBackground = false,
            Name = "UI Thread"
        };

        thread.SetApartmentState(ApartmentState.STA);
        _logger.LogInformation($"GetApartmentState {thread.GetApartmentState()}");
        _uiThreadId = thread.ManagedThreadId;
        thread.Start();

        //このタスクでcontinue withすると、UIスレッドでQueue登録してスレッド終了し、QueueのCOMアクセスが失敗する
        return tcs.Task;
    }

    private void MessageLoop()
    {
        if (_processCancel == null)
        {
            throw new InvalidOperationException($"{nameof(_processCancel)} is null.");
        }

        {
            var result = User32.IsGUIThread(true);
            if (!result)
            {
                var error = Marshal.GetLastPInvokeError();
                throw new WindowException($"IsGUIThread failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }
        }

        { //メッセージキューが無ければ作られるハズ
            var result = 
                User32.GetQueueStatus(
                    0x04FF //QS_ALLINPUT
                );
            var error = Marshal.GetLastPInvokeError();
            _logger.LogInformation($"GetQueueStatus {result} [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
        }

        {
            var si = new Kernel32.STARTUPINFOW()
            {
                cb = Marshal.SizeOf<Kernel32.STARTUPINFOW>()
            };
            Kernel32.GetStartupInfoW(ref si);
            var error = Marshal.GetLastPInvokeError();
            _logger.LogInformation($"GetStartupInfoW [{si.dwFlags}][{si.wShowWindow}] [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
        }

        var forceCancel = false;
        var cancelled = false;

        var ct = _processCancel.Token;

        using var waitHandlesPin = new PinnedBuffer<nint[]>(new nint[] {
            _queueEvent.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
            ct.WaitHandle.SafeWaitHandle.DangerousGetHandle()
         });
        var handleCount = waitHandlesPin.Target.Length;

        _logger.LogWithThreadId(LogLevel.Information, "start message loop", Environment.CurrentManagedThreadId);
        while (true)
        {
            if (forceCancel)
            {
                _logger.LogInformation("force canceled.");
                break;
            }

            if (cancelled)
            {
                if (_windowMap.IsEmpty)
                {
                    _logger.LogInformation("all closed.");
                    break;
                }
            }
            else if (ct.IsCancellationRequested)
            {
                cancelled = true;

                _logger.LogInformation("canceled.");
                CloseAll();

                // 10秒以内にクローズされなければ、ループを終わらせる
                var _ =
                    Task.Delay(10000, CancellationToken.None)
                    .ContinueWith(
                        (task) => { forceCancel = true; },
                        TaskScheduler.Default
                    );
            }

            {
                _logger.LogWithThreadId(LogLevel.Trace, "MsgWaitForMultipleObjectsEx", Environment.CurrentManagedThreadId);
                var res =
                    User32.MsgWaitForMultipleObjectsEx(
                        (uint)handleCount,
                        waitHandlesPin.AddrOfPinnedObject,
                        1000,
                        0x04FF, //QS_ALLINPUT
                        0x0004 //MWMO_INPUTAVAILABLE
                    );
                if (res == 258) // WAIT_TIMEOUT
                {
                    _logger.LogTrace("MsgWaitForMultipleObjectsEx timeout.");
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    _logger.LogTrace("MsgWaitForMultipleObjectsEx comes queue event.");
                    _queueEvent.Reset();
                    //ディスパッチ
                    while (_queue.TryDequeue(out var result))
                    {
                        _logger.LogWithThreadId(LogLevel.Trace, "Invoke", Environment.CurrentManagedThreadId);
                        result.Invoke();
                    }
                    continue;
                }
                else if (res == 1) // WAIT_OBJECT_0+1
                {
                    _logger.LogTrace("MsgWaitForMultipleObjectsEx comes cancel event.");
                    //ctがシグナル状態になりっぱなしになるので、リストから外す
                    handleCount--;
                    continue;
                }
                else if (res == handleCount) // WAIT_OBJECT_0+2
                {
                    _logger.LogTrace("MsgWaitForMultipleObjectsEx comes message.");
                    DispatchMessage();
                    continue;
                }
                else
                {
                    var error = Marshal.GetLastPInvokeError();
                    throw new WindowException($"MsgWaitForMultipleObjectsEx failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
                }
            }
        }
        _logger.LogInformation("end message loop.");
    }

    private void DispatchMessage()
    {
        var msg = new User32.MSG();

        while (true)
        {
            _logger.LogWithThreadId(LogLevel.Trace, "PeekMessage", Environment.CurrentManagedThreadId);
            if (!User32.PeekMessageW(
                    ref msg,
                    nint.Zero,
                    0,
                    0,
                    0x0001 // PM_REMOVE
            ))
            {
                _logger.LogTrace("PeekMessage NONE");
                return;
            }
            _logger.LogTrace($"MSG {msg.hwnd:X} {msg.message:X} {msg.wParam:X} {msg.lParam:X} {msg.time} {msg.pt_x} {msg.pt_y}");

            var IsWindowUnicode = (msg.hwnd != nint.Zero) && User32.IsWindowUnicode(msg.hwnd);
            _logger.LogTrace($"IsWindowUnicode {IsWindowUnicode}");

            {
                var ret = User32.InSendMessageEx(nint.Zero);
                _logger.LogTrace($"InSendMessageEx {ret:X}");
                if ((ret & (0x00000008 | 0x00000001)) == 0x00000001) //ISMEX_SEND
                {
                    _logger.LogTrace("ISMEX_SEND");
                    var ret2 = User32.ReplyMessage(new nint(1));
                    var error = Marshal.GetLastPInvokeError();
                    _logger.LogTrace($"ReplyMessage {ret2} [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
                }
            }

            //TODO: msg.hwnd がnullのときは、↓以降を行っても意味はないらしい？

            {
                _logger.LogTrace("TranslateMessage");
                var ret = User32.TranslateMessage(ref msg);
                var error = Marshal.GetLastPInvokeError();
                _logger.LogTrace($"TranslateMessage {ret} [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }

            {
                _logger.LogTrace("DispatchMessage");
                var ret = IsWindowUnicode
                            ? User32.DispatchMessageW(ref msg)
                            : User32.DispatchMessageA(ref msg)
                            ;
                var error = Marshal.GetLastPInvokeError();
                _logger.LogTrace($"DispatchMessage {ret} [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }
        }
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var isWindowUnicode = (hwnd != nint.Zero) && User32.IsWindowUnicode(hwnd);
        _logger.LogMsgWithThreadId(LogLevel.Trace, "WndProc", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        switch (msg)
        {
            case 0x0081://WM_NCCREATE
                _logger.LogTrace("WM_NCCREATE");

                nint windowHashCode;
                unsafe
                {
                    var cr = Unsafe.AsRef<User32.CREATESTRUCT>((void*)lParam);
                    _logger.LogTrace($"CREATESTRUCT {cr.lpCreateParams:X} {cr.hwndParent:X} {cr.cy} {cr.cx} {cr.y} {cr.x} {cr.style:X} {cr.dwExStyle:X}");
                    windowHashCode = cr.lpCreateParams;
                }

                if (_windowHashMap.TryRemove(windowHashCode, out var window))
                {
                    window._hWindow = hwnd;
                    _windowMap.TryAdd(hwnd, window);
                    _logger.LogInformation($"add window map [{hwnd:X}]");
                }
                else
                {
                    _logger.LogWarning("unkown window handle");
                }
                break;
        }

        {
            if (_windowMap.TryGetValue(hwnd, out var window))
            {
                //ウインドウに流す
                var result = window.WndProc(msg, wParam, lParam, out var handled);
                if (handled)
                {
                    return result;
                }
            }
            else
            {
                _logger.LogTrace("unkown window handle");
            }
        }

        switch (msg)
        {
            case 0x0082://WM_NCDESTROY
                _logger.LogTrace("WM_NCDESTROY");
                if (_windowMap.TryRemove(hwnd, out _))
                {
                    _logger.LogInformation($"remove window map [{hwnd:X}]");
                }
                else
                {
                    _logger.LogWarning($"failed. remove window map [{hwnd:X}]");
                }
                break;
        }

        var defWndProcResult = isWindowUnicode
            ? User32.DefWindowProcW(hwnd, msg, wParam, lParam)
            : User32.DefWindowProcA(hwnd, msg, wParam, lParam)
            ;

        _logger.LogTrace($"DefWindowProc [{defWndProcResult:X}]");
        return defWndProcResult;
    }

}


internal class WindowClass : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private User32.WNDCLASS _windowClass;

    internal nint ClassName => _windowClass.lpszClassName;

    internal nint HInstance => _windowClass.hInstance;

    internal WindowClass(
        ILoggerFactory loggerFactory,
        PinnedDelegate<User32.WNDPROC> wndProc,
        User32.WNDCLASS.CS cs = User32.WNDCLASS.CS.NONE
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowClass>();

        _windowClass = new User32.WNDCLASS
        {
            style = cs,
            lpfnWndProc = wndProc.FunctionPointer,
            hInstance = Kernel32.GetModuleHandleW(default),
            lpszClassName = Marshal.StringToHGlobalUni(nameof(WindowClass) + Guid.NewGuid().ToString())
        };

        var atom = User32.RegisterClassW(ref _windowClass);
        var error = Marshal.GetLastPInvokeError();
        _logger.LogInformation($"RegisterClass {_windowClass} {atom} {error}");
        if (atom == 0)
        {
            throw new WindowException($"RegisterClass failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
        }
    }

    ~WindowClass()
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
            _logger.LogInformation("disposing");
        }

        var result = User32.UnregisterClassW(_windowClass.lpszClassName, _windowClass.hInstance);
        var error = Marshal.GetLastPInvokeError();
        _logger.LogInformation($"UnregisterClass {_windowClass.lpszClassName} {result} [{error} {Marshal.GetPInvokeErrorMessage(error)}]");

        Marshal.FreeHGlobal(_windowClass.lpszClassName);

        _disposed = true;
    }
}

internal class NativeWindow : IWindow
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly WindowManager _windowManager;

    private readonly IWindowManager.OnPreCloseWindow? _onPreCloseWindow;
    private readonly IWindowManager.OnPostPaint? _onPostPaint;

    internal nint _hWindow;
    public nint Handle => _hWindow;

    private readonly ConcurrentDictionary<nint, (nint, PinnedDelegate<User32.WNDPROC>)> _oldWndProcMap = new();
    internal NativeWindow(
        ILoggerFactory loggerFactory,
        WindowManager windowManager,
        IWindowManager.OnPreCloseWindow? onPreCloseWindow = default,
        IWindowManager.OnPostPaint? onPostPaint = default
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<NativeWindow>();
        _windowManager = windowManager;

        _onPreCloseWindow = onPreCloseWindow;
        _onPostPaint = onPostPaint;

        _logger.LogInformation("Create end");
    }

    public T Dispatch<T>(Func<T> item)
    {
        return _windowManager.Dispatch(item);
    }

    internal void CreateWindow(
        WindowClass windowClass
    )
    {
        var thisHashCode = GetHashCode();

        _hWindow = Dispatch(() => {
            var style = unchecked((int)
                0x80000000 //WS_POPUP
                           // 0x00000000 //WS_OVERLAPPED
                           // | 0x00C00000 //WS_CAPTION
                           // | 0x00080000 //WS_SYSMENU
                           // | 0x00040000 //WS_THICKFRAME
                | 0x10000000 //WS_VISIBLE
                );

            var CW_USEDEFAULT = unchecked((int)0x80000000);

            _logger.LogWithThreadId(LogLevel.Trace, "CreateWindowEx", Environment.CurrentManagedThreadId);
            var hWindow =
                User32.CreateWindowExW(
                    0,
                    windowClass.ClassName,
                    nint.Zero,
                    style,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    nint.Zero,
                    nint.Zero,
                    windowClass.HInstance,
                    new nint(thisHashCode)
                );
            var error = Marshal.GetLastPInvokeError();
            _logger.LogWithHWndAndErrorId(LogLevel.Information, "CreateWindowEx result", hWindow, error);
            if (hWindow == nint.Zero)
            {
                hWindow = default;
                throw new WindowException($"CreateWindowEx failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }

            return hWindow;
        });
    }

    public bool Close()
    {
        _logger.LogInformation($"Close {_hWindow:X}");
        return SendMessage(
            0x0010, //WM_CLOSE
            nint.Zero,
            nint.Zero
        );
    }

    private bool SendMessage(
        uint nMsg,
        nint wParam,
        nint lParam
    )
    {
        _logger.LogMsgWithThreadId(LogLevel.Trace, "SendMessageW", _hWindow, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
        var _ =
            User32.SendMessageW(
                _hWindow,
                nMsg,
                wParam,
                lParam
            );
        var error = Marshal.GetLastPInvokeError();
        if (error != 0)
        {
            _logger.LogWithHWndAndErrorId(LogLevel.Error, "SendMessageW", _hWindow, error);
            return false;
        }
        return true;
    }

    public bool Move(
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        return Dispatch(() =>
        {
            _logger.LogInformation($"MoveWindow {_hWindow:X} {x} {y} {width} {height} {repaint} current {Environment.CurrentManagedThreadId:X}");
            var result =
                User32.MoveWindow(
                    _hWindow,
                    x,
                    y,
                    width,
                    height,
                    repaint
                );

            if (!result)
            {
                _logger.LogWithHWndAndErrorId(LogLevel.Error, "MoveWindow", _hWindow, Marshal.GetLastPInvokeError());
            }
            return result;
        });
    }

    public bool Show(
        int cmdShow
    )
    {
        return Dispatch(() =>
        {
            _logger.LogInformation($"ShowWindow {_hWindow:X} {cmdShow} current {Environment.CurrentManagedThreadId:X}");
            var result =
                User32.ShowWindow(
                    _hWindow,
                    cmdShow
                );

            var error = Marshal.GetLastPInvokeError();

            //result=0: 実行前は非表示だった/ <>0:実行前から表示されていた
            _logger.LogInformation($"ShowWindow {_hWindow:X} {result} {error}");

            if (error == 1400) // ERROR_INVALID_WINDOW_HANDLE
            {
                throw new WindowException($"ShowWindow failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }

            {
                var wndpl = new User32.WINDOWPLACEMENT()
                {
                    length = Marshal.SizeOf<User32.WINDOWPLACEMENT>()
                };
                User32.GetWindowPlacement(_hWindow, ref wndpl);

                _logger.LogInformation($"GetWindowPlacement result {cmdShow} -> {wndpl.showCmd}");
            }

            return result;
        });
    }

    public void SetWindowStyle(int style)
    {
        SetWindowStyle(-16, new nint(style)); //GWL_STYLE
    }

    private void SetWindowStyle(int nIndex, nint dwNewLong)
    {
        var _ = Dispatch(() =>
        {
            var clientRect = new User32.RECT();

            {
                var result = User32.GetClientRect(_hWindow, ref clientRect);

                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "GetClientRect failed", _hWindow, Marshal.GetLastPInvokeError());
                    return false;
                }
            }

            {
                var result = User32.AdjustWindowRect(ref clientRect, dwNewLong.ToInt32(), false);

                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "AdjustWindowRect failed", _hWindow, Marshal.GetLastPInvokeError());
                    return false;
                }
            }

            {
                Marshal.SetLastPInvokeError(0);
                _logger.LogInformation($"SetWindowLong {_hWindow:X} {nIndex:X} {dwNewLong:X} current {Environment.CurrentManagedThreadId:X}");
                var result =
                    Environment.Is64BitProcess
                        ? User32.SetWindowLongPtrW(_hWindow, nIndex, dwNewLong)
                        : User32.SetWindowLongW(_hWindow, nIndex, dwNewLong);

                var error = Marshal.GetLastPInvokeError();

                if (result == nint.Zero && error != 0)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "SetWindowLong failed", _hWindow, error);
                    return false;
                }
            }

            {
                var width = clientRect.right - clientRect.left;
                var height = clientRect.bottom - clientRect.top;

                _logger.LogInformation($"SetWindowPos {_hWindow:X} current {Environment.CurrentManagedThreadId:X}");
                var result =
                    User32.SetWindowPos(
                            _hWindow,
                            nint.Zero,
                            0,
                            0,
                            width,
                            height,
                            0x0002 //SWP_NOMOVE
                            //| 0x0001 //SWP_NOSIZE
                            | 0x0004 //SWP_NOZORDER
                            | 0x0020 //SWP_FRAMECHANGED
                        );

                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "SetWindowPos failed", _hWindow, Marshal.GetLastPInvokeError());
                    return false;
                }
            }

            return true;
        });
    }

    internal nint WndProc(uint msg, nint wParam, nint lParam, out bool handled)
    {
        handled = false;
        _logger.LogMsgWithThreadId(LogLevel.Trace, "WndProc", _hWindow, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        switch (msg)
        {
            case 0x0082://WM_NCDESTROY
                _logger.LogTrace("WM_NCDESTROY");
                _hWindow = default;
                break;

            case 0x0010://WM_CLOSE
                _logger.LogTrace("WM_CLOSE");
                try
                {
                    _onPreCloseWindow?.Invoke();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "onPreCloseWindow error");
                }

                _logger.LogTrace($"DestroyWindow {_hWindow:X} current {Environment.CurrentManagedThreadId:X}");
                var result = User32.DestroyWindow(_hWindow);
                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "DestroyWindow", _hWindow, Marshal.GetLastPInvokeError());
                }

                handled = true;
                break;

            case 0x0210://WM_PARENTNOTIFY
                _logger.LogTrace("WM_PARENTNOTIFY");
                switch (wParam & 0xFFFF)
                {
                    case 0x0001: //WM_CREATE
                        {
                            _logger.LogTrace($"WM_PARENTNOTIFY WM_CREATE {wParam:X}");
                            var childHWnd = lParam;
                            var isChildeWindowUnicode = (lParam != nint.Zero) && User32.IsWindowUnicode(childHWnd);
                            var subWndProc = new PinnedDelegate<User32.WNDPROC>(new(SubWndProc));
                            var oldWndProc = isChildeWindowUnicode
                                                ? Environment.Is64BitProcess
                                                    ? User32.SetWindowLongPtrW(childHWnd, -4, subWndProc.FunctionPointer) //GWLP_WNDPROC
                                                    : User32.SetWindowLongW(childHWnd, -4, subWndProc.FunctionPointer)
                                                : Environment.Is64BitProcess
                                                    ? User32.SetWindowLongPtrA(childHWnd, -4, subWndProc.FunctionPointer)
                                                    : User32.SetWindowLongA(childHWnd, -4, subWndProc.FunctionPointer)
                                                ;
                            _oldWndProcMap.TryAdd(childHWnd, (oldWndProc, subWndProc));

                            break;
                        }
                    case 0x0002: //WM_DESTROY
                        {
                            _logger.LogTrace($"WM_PARENTNOTIFY WM_DESTROY {wParam:X}");
                            var childHWnd = lParam;
                            if (_oldWndProcMap.TryRemove(childHWnd, out var pair))
                            {
                                var isChildeWindowUnicode = (lParam != nint.Zero) && User32.IsWindowUnicode(childHWnd);
                                var _ = isChildeWindowUnicode
                                                ? Environment.Is64BitProcess
                                                    ? User32.SetWindowLongPtrW(childHWnd, -4, pair.Item1) //GWLP_WNDPROC
                                                    : User32.SetWindowLongW(childHWnd, -4, pair.Item1)
                                                : Environment.Is64BitProcess
                                                    ? User32.SetWindowLongPtrA(childHWnd, -4, pair.Item1)
                                                    : User32.SetWindowLongA(childHWnd, -4, pair.Item1)
                                                ;

                                pair.Item2.Dispose();
                            }

                            break;
                        }
                }

                break;
        }
        return nint.Zero;
    }

    private nint SubWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        _logger.LogMsgWithThreadId(LogLevel.Trace, "SubWndProc", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        var isWindowUnicode = (hwnd != nint.Zero) && User32.IsWindowUnicode(hwnd);
        nint result;

        if (_oldWndProcMap.TryGetValue(hwnd, out var pair))
        {
            _logger.LogMsgWithThreadId(LogLevel.Trace, "CallWindowProc", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);
            result = isWindowUnicode
                        ? User32.CallWindowProcW(pair.Item1, hwnd, msg, wParam, lParam)
                        : User32.CallWindowProcA(pair.Item1, hwnd, msg, wParam, lParam)
                        ;
        }
        else
        {
            _logger.LogWarning("unkown hwnd -> DefWindowProc");
            result = isWindowUnicode
                        ? User32.DefWindowProcW(hwnd, msg, wParam, lParam)
                        : User32.DefWindowProcA(hwnd, msg, wParam, lParam)
                        ;
        }

        switch (msg)
        {
            case 0x000F://WM_PAINT
                _logger.LogTrace("SubWndProc WM_PAINT");
                try
                {
                    _onPostPaint?.Invoke(hwnd);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "onPostPaint error");
                }
                break;

            default:
                break;
        }

        return result;
    }

}
        /*
        //表示していないとwinrt::hresult_invalid_argumentになる
        var item = GraphicsCaptureItemInterop.CreateForWindow(hWindow);
        item.Closed += (item, obj) => {
            Logger.LogInformation("[window] GraphicsCaptureItem closed");
        };

        unsafe
        {

            Windows.Win32.Graphics.Direct3D11.ID3D11Device* d;

            Windows.Win32.PInvoke.D3D11CreateDevice(
                null,
                Windows.Win32.Graphics.Direct3D.D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                null,
                Windows.Win32.Graphics.Direct3D11.D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                null,
                11,
                &d,
                null,
                null
                );
            Windows.Win32.PInvoke.CreateDirect3D11DeviceFromDXGIDevice(null, a.ObjRef);
        }

        IInspectable a;

        IDirect3DDevice canvas;

        using var pool =
            Direct3D11CaptureFramePool.Create(
                canvas,
                Windows.Graphics.DirectX.DirectXPixelFormat.R8G8B8A8UIntNormalized,
                2,
                item.Size
            );

        pool.FrameArrived += (pool, obj) => {
            using var frame = pool.TryGetNextFrame();
            //frame.Surface;
            Logger.LogInformation("[window] FrameArrived");

        };

        using var session = pool.CreateCaptureSession(item);
        session.StartCapture();
        Logger.LogInformation("[window] StartCapture");
        */