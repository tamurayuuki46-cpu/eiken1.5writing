using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;
using EikenSpelling.Persistence;

namespace EikenSpelling.Flow;

/// <summary>起動・再読込の結果。Screen が FatalScreen・BothCorruptScreen のとき Document は null。</summary>
internal sealed record StartupResult(
    FlowScreen Screen,
    LearningSettings Settings,
    WordSet? WordSet,
    ProgressRepository? Repository,
    ProgressDocument? Document);

/// <summary>
/// 起動(app-flow design 4.2): 保存先 → 設定 → 単語データ → 学習記録 の順に読み、最初の画面を決める。
/// 何も書かない(書くのは利用者の操作の後だけ)。
/// </summary>
internal static class FlowStartup
{
    public static StartupResult Load(FlowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var defaults = DefaultSettings.Create();

        var directory = options.DataDirectory;
        if (string.IsNullOrEmpty(directory) || !Path.IsPathFullyQualified(directory))
        {
            return Fatal(FatalKind.FolderInvalid, "保存先のフォルダが正しくありません。", defaults);
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
            return Fatal(FatalKind.WordSetInvalid, "単語データを読み込めません。アプリを入れ直してください。", settings);
        }

        var repository = new ProgressRepository(directory, options.Time, options.Files, options.RetryDelay);
        var load = repository.Load(settings);
        switch (load.Status)
        {
            case LoadStatus.BothCorrupt:
                return new(new BothCorruptScreen { Notices = notices.ToImmutable() }, settings, wordSet, repository, null);
            case LoadStatus.NewerVersion:
                return Fatal(FatalKind.NewerVersion, "新しい版のアプリで作られた学習記録です。新しい版のアプリで開いてください。", settings);
            case LoadStatus.Unavailable:
                return Fatal(FatalKind.Unavailable, "学習記録のファイルを読めません。ほかのアプリが使っていないか確かめてください。", settings);
        }

        if (load.Status == LoadStatus.RecoveredFromBackup)
        {
            notices.Add("学習記録が壊れていたため、バックアップから復旧しました。最後の数問の記録が失われた可能性があります。");
        }

        notices.AddRange(load.Warnings);
        var document = load.Document ?? new ProgressDocument();
        return new(new HomeScreen { Notices = notices.ToImmutable() }, settings, wordSet, repository, document);
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

    private static StartupResult Fatal(FatalKind kind, string reason, LearningSettings settings) =>
        new(new FatalScreen(kind, reason), settings, null, null, null);
}
