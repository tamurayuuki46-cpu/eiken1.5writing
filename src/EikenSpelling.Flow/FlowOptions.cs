using EikenSpelling.Core.Models;
using EikenSpelling.Persistence;

namespace EikenSpelling.Flow;

/// <summary>起動に渡すもの(app-flow design 4.2)。ファイルの場所と単語データの読み込みは呼び出し側(WPF)が決める。</summary>
public sealed record FlowOptions
{
    /// <summary>保存先フォルダ。完全な絶対パスだけ受け付ける(それ以外は FatalScreen(FolderInvalid))。</summary>
    public required string? DataDirectory { get; init; }

    /// <summary>単語データ(words.json)の中身。</summary>
    public required string? WordSetJson { get; init; }

    /// <summary>この端末で使えるモード(音声が無い端末では Audio を含めない)。</summary>
    public required IReadOnlySet<LearningMode> AvailableModes { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>出題の選択・並びに使う乱数。1スレッドから使う前提。</summary>
    public Random Random { get; init; } = new();

    public Func<Guid> NewSessionId { get; init; } = Guid.NewGuid;

    /// <summary>テスト用: ファイル操作の差し替え。</summary>
    public IFileOperations? Files { get; init; }

    /// <summary>テスト用: 保存の再試行の待ち時間。</summary>
    public TimeSpan? RetryDelay { get; init; }
}

/// <summary>練習設定画面の選択(v1.0 16.1、v1.2 2章 A)。その練習だけに使い、settings.json には書かない。</summary>
public sealed record PracticeChoice(bool AutoMix, LearningMode ManualMode, int QuestionCount, bool WeakWordsOnly, double FlashSeconds);
