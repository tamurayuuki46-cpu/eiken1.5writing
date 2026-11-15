using System.Windows.Threading;

using EikenSpelling.Presentation.Scheduling;

namespace EikenSpelling.Wpf;

/// <summary>DispatcherTimer で一定時間後に UI スレッドで1回だけ実行する(フラッシュの表示秒数)。Dispose で取り消す。</summary>
public sealed class DispatcherScheduler(Dispatcher dispatcher) : IUiScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
        return new Handle(timer);
    }

    private sealed class Handle(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
