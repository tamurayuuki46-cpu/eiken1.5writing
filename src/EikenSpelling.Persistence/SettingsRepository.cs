using EikenSpelling.Core.Settings;

namespace EikenSpelling.Persistence;

/// <summary>読込の結果。Settings は常に値がある(Loaded・RecoveredFromBackup 以外は初期値)。</summary>
public sealed record SettingsLoadResult(LoadStatus Status, LearningSettings Settings, string? Reason);

public enum SettingsSaveStatus
{
    Saved,
    Invalid,
    Failed,
}

public sealed record SettingsSaveResult(SettingsSaveStatus Status, IReadOnlyList<SettingsIssue> Issues, string? Reason);

/// <summary>
/// settings.json の持ち主(Data Owner)。学習記録には触れない。
/// 値が不正・壊れているときは初期値で動かす(ファイルは書かない)。新しい版・読めないときは保存しない。
/// </summary>
public sealed class SettingsRepository
{
    public const string FileName = "settings.json";

    private readonly AtomicJsonFile<LearningSettings> _file;
    private readonly IFileOperations _files;
    private LoadStatus? _state;

    public SettingsRepository(string directory, TimeProvider? time = null, IFileOperations? files = null, TimeSpan? retryDelay = null)
    {
        _files = files ?? SystemFileOperations.Instance;
        _file = new AtomicJsonFile<LearningSettings>(directory, FileName, Read, _files, time, retryDelay);
    }

    public SettingsLoadResult Load()
    {
        try
        {
            _files.CreateDirectory(_file.Directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _state = LoadStatus.Unavailable;
            return new(LoadStatus.Unavailable, DefaultSettings.Create(), "保存先フォルダを用意できません。");
        }

        var load = _file.Load();
        _state = load.Status;
        var settings = load.Status is LoadStatus.Loaded or LoadStatus.RecoveredFromBackup && load.Value is not null
            ? load.Value
            : DefaultSettings.Create();
        return new(load.Status, settings, load.Reason);
    }

    /// <summary>不正な設定は保存せず問題の一覧を返す。語彙データの確認は練習の開始時に行う。</summary>
    public SettingsSaveResult Save(LearningSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_state is null or LoadStatus.NewerVersion or LoadStatus.Unavailable)
        {
            return new(SettingsSaveStatus.Failed, [], _state is null
                ? "先に設定を読み込んでください。"
                : "読み込みの結果(新しい版・読めない)のため、設定を上書きしません。");
        }

        var issues = SettingsValidator.Validate(settings);
        if (issues.Count > 0)
        {
            return new(SettingsSaveStatus.Invalid, issues, "設定に不正な値があるため保存していません。");
        }

        var json = LearningSettingsJson.Serialize(settings);
        var result = _state == LoadStatus.BothCorrupt ? _file.EvacuateAndSave(json) : _file.Save(json);
        if (!result.Succeeded)
        {
            return new(SettingsSaveStatus.Failed, [], result.Reason);
        }

        _state = LoadStatus.Loaded;
        return new(SettingsSaveStatus.Saved, [], null);
    }

    private static FileRead<LearningSettings> Read(string json)
    {
        LearningSettings settings;
        try
        {
            settings = LearningSettingsJson.Parse(json);
        }
        catch (InvalidDataException)
        {
            return FileRead<LearningSettings>.Corrupt("設定ファイルを読み取れません。");
        }

        if (settings.SettingsVersion > LearningSettings.CurrentVersion)
        {
            return FileRead<LearningSettings>.Newer($"新しい版のアプリで作られた設定です(settingsVersion {settings.SettingsVersion})。");
        }

        var issues = SettingsValidator.Validate(settings);
        return issues.Count > 0
            ? FileRead<LearningSettings>.Corrupt("設定に不正な値があります: " + string.Join(", ", issues.Select(i => i.Path).Distinct()))
            : FileRead<LearningSettings>.Ok(settings);
    }
}
