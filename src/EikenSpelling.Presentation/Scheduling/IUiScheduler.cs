namespace EikenSpelling.Presentation.Scheduling;

/// <summary>
/// 一定時間後に UI スレッドで1回だけ実行する差し替え口(フラッシュの表示秒数)。Wpf は DispatcherTimer、テストは手で進める実装。
/// 返した IDisposable を Dispose すると取り消す(画面が替わったとき)。
/// </summary>
public interface IUiScheduler
{
    IDisposable Schedule(TimeSpan delay, Action action);
}

/// <summary>何も実行しない実装(既定)。</summary>
public sealed class NoUiScheduler : IUiScheduler
{
    public static readonly NoUiScheduler Instance = new();

    public IDisposable Schedule(TimeSpan delay, Action action) => Nothing.Instance;

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
