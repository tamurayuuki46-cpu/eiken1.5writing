using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;
using EikenSpelling.Persistence;

namespace EikenSpelling.Flow;

/// <summary>
/// 起動・再読込の結果。Stop があれば練習できない(FatalScreen・BothCorruptScreen)。
/// Stop が無ければ Document を読めている。最初の画面(ホーム / 再開の選択)は PracticeFlow が決める。
/// </summary>
internal sealed record StartupResult(
    FlowScreen? Stop,
    ImmutableArray<string> Notices,
    LearningSettings Settings,
    WordSet? WordSet,
    ProgressRepository? Repository,
    ProgressDocument? Document);

/// <summary>
/// 起動(app-flow design 4.2): 保存先 → 設定 → 単語データ → 学習記録 の順に読む。
/// 何も書かない(書くのは利用者の操作の後だけ)。
/// </summary>
internal static class FlowStartup
{
    public static StartupResult Load(FlowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var directory = options.DataDirectory;
        if (string.IsNullOrEmpty(directory) || !Path.IsPathFullyQualified(directory))
        {
            return Stop(new FatalScreen(FatalKind.FolderInvalid, "保存先のフォルダが正しくありません。"), [], DefaultSettings.Create());
        }

        var notices = ImmutableArray.CreateBuilder<string>();

        var settingsLoad = new SettingsRepository(directory, options.Time, options.Files, options.RetryDelay).Load();
        var settings = settingsLoad.Settings;
        var settingsNotice = settingsLoad.Status switch
        {
            LoadStatus.RecoveredFromBackup => "設定ファイルが壊れていたため、バックアップから復元しました。",
            LoadStatus.BothCorrupt => "設定ファイルが壊れているため、初期設定で動いています。",
            LoadStatus.NewerVersion => "新しい版のアプリで作られた設定のため、初期設定で動いています。",
            LoadStatus.Unavailable => "設定ファイルを読めないため、初期設定で動いています。",
            _ => null,
        };
        if (settingsNotice is not null)
        {
            notices.Add(settingsNotice);
        }

        var wordSet = ReadWordSet(options.WordSetJson);
        if (wordSet is null)
        {
            return Stop(new FatalScreen(FatalKind.WordSetInvalid, "単語データを読み込めません。アプリを入れ直してください。"), [], settings);
        }

        var repository = new ProgressRepository(directory, options.Time, options.Files, options.RetryDelay);
        var load = repository.Load(settings);
        FlowScreen? stop = load.Status switch
        {
            LoadStatus.BothCorrupt => new BothCorruptScreen(),
            LoadStatus.NewerVersion => new FatalScreen(
                FatalKind.NewerVersion, "新しい版のアプリで作られた学習記録です。新しい版のアプリで開いてください。"),
            LoadStatus.Unavailable => new FatalScreen(
                FatalKind.Unavailable, "学習記録のファイルを読めません。ほかのアプリが使っていないか確かめてください。"),
            _ => null,
        };
        if (stop is not null)
        {
            var kept = stop is BothCorruptScreen ? notices.ToImmutable() : [];
            return new(stop with { Diagnostic = load.Reason }, kept, settings, wordSet, repository, null);
        }

        if (load.Status == LoadStatus.RecoveredFromBackup)
        {
            notices.Add("学習記録が壊れていたため、バックアップから復旧しました。最後の数問の記録が失われた可能性があります。");
        }

        // 単語ごとの警告は同じ文言が並びうるのでまとめる
        notices.AddRange(load.Warnings.Distinct(StringComparer.Ordinal));
        return new(null, notices.ToImmutable(), settings, wordSet, repository, load.Document ?? new ProgressDocument());
    }

    private static WordSet? ReadWordSet(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        WordSet set;
        try
        {
            set = WordSetLoader.Parse(json);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return WordSetValidator.Validate(set).Any(i => i.Severity == ValidationSeverity.Error) ? null : set;
    }

    private static StartupResult Stop(FlowScreen screen, ImmutableArray<string> notices, LearningSettings settings) =>
        new(screen, notices, settings, null, null, null);
}
