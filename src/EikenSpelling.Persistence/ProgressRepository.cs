using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Persistence;

public sealed record ProgressLoadResult(LoadStatus Status, ProgressDocument? Document, IReadOnlyList<string> Warnings, string? Reason);

/// <summary>
/// progress.json の持ち主(Data Owner)。呼び出し順は 設定の Load → 学習記録の Load。
/// Load の前、または NewerVersion・Unavailable・BothCorrupt(StartFresh 前)の後は保存しない(上書きしない)。
/// </summary>
public sealed class ProgressRepository
{
    public const string FileName = "progress.json";

    private readonly AtomicJsonFile<ProgressDocument> _file;
    private readonly IFileOperations _files;
    private readonly TimeProvider _time;
    private LearningSettings _settings = DefaultSettings.Create();
    private LoadStatus? _state;

    public ProgressRepository(string directory, TimeProvider? time = null, IFileOperations? files = null, TimeSpan? retryDelay = null)
    {
        _time = time ?? TimeProvider.System;
        _files = files ?? SystemFileOperations.Instance;
        _file = new AtomicJsonFile<ProgressDocument>(directory, FileName, Read, _files, _time, retryDelay);
    }

    public ProgressLoadResult Load(LearningSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        try
        {
            _files.CreateDirectory(_file.Directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _state = LoadStatus.Unavailable;
            return new(LoadStatus.Unavailable, null, [], "保存先フォルダを用意できません。");
        }

        var load = _file.Load();
        _state = load.Status;
        var document = load.Status == LoadStatus.FirstRun ? new ProgressDocument() : load.Value;
        return new(load.Status, document, load.Warnings, load.Reason);
    }

    public SaveResult Save(ProgressDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_state is not (LoadStatus.Loaded or LoadStatus.FirstRun or LoadStatus.RecoveredFromBackup))
        {
            return SaveResult.Failure(_state is null
                ? "先に学習記録を読み込んでください。"
                : "読み込みの結果(新しい版・読めない・両方破損)のため、上書きしません。");
        }

        string json;
        try
        {
            json = ProgressJson.Serialize(document);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SaveResult.Failure("学習記録を保存用の形に変換できませんでした。");
        }

        return _file.Save(json);
    }

    /// <summary>BothCorrupt の後、利用者が確認したときだけ呼ぶ。壊れたファイルは退避名で残す。</summary>
    public SaveResult StartFresh(string wordSetVersion, string sourceVersion)
    {
        if (_state != LoadStatus.BothCorrupt)
        {
            return SaveResult.Failure("新しい記録で始められるのは、両方のファイルが壊れているときだけです。");
        }

        var result = _file.EvacuateAndSave(ProgressJson.Serialize(new ProgressDocument
        {
            WordSetVersion = wordSetVersion,
            SourceVersion = sourceVersion,
        }));
        if (result.Succeeded)
        {
            _state = LoadStatus.Loaded;
        }

        return result;
    }

    private FileRead<ProgressDocument> Read(string json)
    {
        int version;
        try
        {
            version = ProgressJson.ReadSchemaVersion(json);
        }
        catch (InvalidDataException)
        {
            return FileRead<ProgressDocument>.Corrupt("学習記録を読み取れません。");
        }

        if (version > ProgressDocument.CurrentSchemaVersion)
        {
            return FileRead<ProgressDocument>.Newer($"新しい版のアプリで作られた学習記録です(schemaVersion {version})。");
        }

        ProgressDocument document;
        try
        {
            document = ProgressJson.Parse(json);
        }
        catch (InvalidDataException)
        {
            return FileRead<ProgressDocument>.Corrupt("学習記録を読み取れません。");
        }

        var issues = ProgressValidator.Validate(document, _settings, _time.GetUtcNow());
        if (issues.Any(i => i.Severity == ProgressIssueSeverity.Critical))
        {
            return FileRead<ProgressDocument>.Corrupt("学習記録の内容が成り立っていません: "
                + string.Join(", ", issues.Where(i => i.Severity == ProgressIssueSeverity.Critical).Select(i => i.Code).Distinct()));
        }

        return FileRead<ProgressDocument>.Ok(document, issues.Select(i => i.Message).ToList());
    }
}
