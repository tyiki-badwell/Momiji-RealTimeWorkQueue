using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Momiji.Core.Buffer;

public abstract class InternalGCHandleBuffer<T> : IDisposable where T : notnull
{
    private bool _disposed;
    protected GCHandle Handle { get; }

    protected InternalGCHandleBuffer([DisallowNull]T buffer, GCHandleType handleType)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Handle = GCHandle.Alloc(buffer, handleType);
    }

    ~InternalGCHandleBuffer()
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

        if (Handle.IsAllocated)
        {
            Handle.Free();
        }

        _disposed = true;
    }
}

public partial class PinnedBuffer<T>(
    T buffer
) : InternalGCHandleBuffer<T>(buffer, GCHandleType.Pinned) where T : notnull
{
    [NotNull]
    public T Target
    {
        get
        {
            var result = Handle.Target;
            if (result == default)
            {
                throw new InvalidOperationException("Target is null.");
            }
            return (T)result;
        }
    }
    public nint AddrOfPinnedObject => Handle.AddrOfPinnedObject();

    public int SizeOf => Marshal.SizeOf<T>();
}

public partial class PinnedDelegate<T>(
    T buffer
) : InternalGCHandleBuffer<T>(buffer, GCHandleType.Normal) where T : notnull, Delegate
{
    public nint FunctionPointer
    {
        get
        {
            if (Handle.Target == default)
            {
                return default;
            }
            else
            {
                return Marshal.GetFunctionPointerForDelegate(Handle.Target);
            }
        }
    }
}
