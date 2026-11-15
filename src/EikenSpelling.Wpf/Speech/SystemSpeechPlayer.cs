using System.Globalization;
using System.Speech.Synthesis;
using System.Windows.Threading;

using EikenSpelling.Presentation.Speech;

namespace EikenSpelling.Wpf.Speech;

/// <summary>
/// System.Speech による英単語の読み上げ(音声モード design 2章)。起動時に有効な英語(en-*)の声を選ぶ(en-US を優先)。
/// Speak は前の読み上げを止めてから非同期で読む(UI を止めない)。失敗は UI スレッドで onFailed に渡す(理由は例外の型名だけ)。
/// </summary>
public sealed class SystemSpeechPlayer : ISpeechPlayer, IDisposable
{
    private readonly SpeechSynthesizer? _synth;
    private readonly Dispatcher _dispatcher;
    private Action<string>? _onFailed;
    private Prompt? _current;

    private SystemSpeechPlayer(SpeechSynthesizer? synth, Dispatcher dispatcher)
    {
        _synth = synth;
        _dispatcher = dispatcher;
        if (_synth is not null)
        {
            _synth.SpeakCompleted += OnSpeakCompleted;
        }
    }

    public bool IsAvailable => _synth is not null;

    /// <summary>英語の声を探す。見つからない・調べられないときは使えない実装を返す(reason に型名)。</summary>
    public static SystemSpeechPlayer Create(Dispatcher dispatcher, out string? reason)
    {
        reason = null;
        SpeechSynthesizer? synth = null;
        try
        {
            synth = new SpeechSynthesizer();
            var voices = synth.GetInstalledVoices()
                .Where(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en")
                .OrderByDescending(v => v.VoiceInfo.Culture.Name == "en-US")
                .ToList();
            if (voices.Count == 0)
            {
                synth.Dispose();
                reason = "no-english-voice";
                return new SystemSpeechPlayer(null, dispatcher);
            }

            synth.SelectVoice(voices[0].VoiceInfo.Name);
            synth.SetOutputToDefaultAudioDevice();
            return new SystemSpeechPlayer(synth, dispatcher);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            synth?.Dispose();
            reason = ex.GetType().Name;
            return new SystemSpeechPlayer(null, dispatcher);
        }
    }

    public void Speak(string text, Action<string> onFailed)
    {
        if (_synth is null)
        {
            onFailed("unavailable");
            return;
        }

        try
        {
            _synth.SpeakAsyncCancelAll();
            _onFailed = onFailed;
            var builder = new PromptBuilder(CultureInfo.GetCultureInfo("en-US"));
            builder.AppendText(text);
            var prompt = new Prompt(builder);
            _current = prompt;
            _synth.SpeakAsync(prompt);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            onFailed(ex.GetType().Name);
        }
    }

    public void Stop()
    {
        try
        {
            _onFailed = null;
            _synth?.SpeakAsyncCancelAll();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 止められなくても画面の切り替えは続ける
        }
    }

    public void Dispose()
    {
        Stop();
        _synth?.Dispose();
    }

    /// <summary>完了イベントは別のスレッドから来ることがあるので、判定ごと UI スレッドで行う(_current・_onFailed は UI スレッドだけで読み書き)。</summary>
    private void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
    {
        var failed = e.Error is not null && !e.Cancelled;
        var reason = e.Error?.GetType().Name ?? "";
        var prompt = e.Prompt;
        _dispatcher.BeginInvoke(() =>
        {
            if (failed && ReferenceEquals(prompt, _current))
            {
                _onFailed?.Invoke(reason); // 成功・取り消し・前の読み上げの完了は何もしない
            }
        });
    }
}
