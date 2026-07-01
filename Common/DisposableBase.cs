namespace Quotix.Common;

/// <summary>
/// 可释放基类 — 实现标准 IDisposable 模式，子类重写 ReleaseManaged/ReleaseUnmanaged
/// </summary>
public abstract class DisposableBase : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        ReleaseManaged();
        ReleaseUnmanaged();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>释放托管资源</summary>
    protected virtual void ReleaseManaged() { }

    /// <summary>释放非托管资源</summary>
    protected virtual void ReleaseUnmanaged() { }

    /// <summary>检查是否已释放</summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }
}
