using System.Threading;

namespace EikenSpelling.Wpf;

/// <summary>
/// 二重起動の防止(design 4.2、DECISIONS「保存・復旧」)。名前付き Mutex を終了まで持つ。
/// 2つ目の起動は名前付きイベントで1つ目に合図し、何もせずに終わる。1つ目は合図を受けたら窓を前に出す。
/// 限界: ユーザーの切り替えで別のログオンセッションからは防げない / 同じ名前を先に作られると起動できない。
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\EikenSpelling.SingleInstance";
    private const string ActivateName = @"Local\EikenSpelling.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly bool _owned;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, bool owned)
    {
        _mutex = mutex;
        _activate = activate;
        _owned = owned;
    }

    public bool IsFirst => _owned;

    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // 前のプロセスが異常終了した後は取得できたとみなす
        }

        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
        return new SingleInstance(mutex, activate, owned);
    }

    /// <summary>2つ目の起動: 1つ目に「前に出て」と合図する。</summary>
    public void SignalFirst() => _activate.Set();

    /// <summary>1つ目: 合図を受けるたびに onActivate を呼ぶ(スレッドプールから呼ばれる)。</summary>
    public void ListenForActivation(Action onActivate) =>
        _registration = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _registration?.Unregister(null);
        if (_owned)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 別のスレッドから呼ばれた場合など。終了時なので無視する
            }
        }

        _mutex.Dispose();
        _activate.Dispose();
    }
}
