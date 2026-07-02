namespace Quotix.Common;

/// <summary>
/// 标准 IDisposable 模式的抽象基类。
/// 子类只需重写 ReleaseManaged() 和/或 ReleaseUnmanaged()。
/// </summary>
public abstract class DisposableBase : IDisposable
{
    private bool _disposed;

    /// <summary>释放资源（公开接口）</summary>
    public void Dispose()
    {
        if (_disposed) return;
        ReleaseManaged();
        ReleaseUnmanaged();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>释放托管资源（子类重写）</summary>
    protected virtual void ReleaseManaged() { }

    /// <summary>释放非托管资源（子类重写）</summary>
    protected virtual void ReleaseUnmanaged() { }

    /// <summary>检查对象是否已释放，已释放则抛出异常</summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }
}
