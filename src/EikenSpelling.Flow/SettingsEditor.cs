using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;
using EikenSpelling.Persistence;

namespace EikenSpelling.Flow;

/// <summary>
/// 今の設定と、設定画面の保存(practice-settings design 4.3)。今の設定は保存に成功したときだけ置き換える。
/// 保存できるかの正本は SettingsRepository.CanSave(ここでは同じ判定を書かない)。
/// </summary>
internal sealed class SettingsEditor(SettingsRepository? repository, LearningSettings current)
{
    private const string SaveFailedMessage = "設定を保存できませんでした。もう一度保存するか、保存せずに閉じてください。";
    private const string InvalidMessage = "設定に正しくない値があります。";

    public LearningSettings Current { get; private set; } = current;

    public PracticeChoice Defaults => PracticeChoiceApplier.FromSettings(Current);

    public SettingsScreen Open(IReadOnlySet<LearningMode> availableModes)
    {
        var practice = Current.Practice;
        var canSave = repository?.CanSave ?? false;
        return new SettingsScreen(
            Defaults,
            [.. practice.QuestionCountChoices],
            [.. practice.FlashSecondsChoices],
            [.. Enum.GetValues<LearningMode>().Where(availableModes.Contains)],
            canSave,
            canSave ? null : CannotSaveReason(repository?.Status),
            canSave && repository?.Status == LoadStatus.BothCorrupt
                ? "設定ファイルが壊れているため、保存すると詳細設定は初期値になります(元のファイルは残します)。"
                : null);
    }

    /// <summary>画面の値を基本設定の初期値にする(保存はしない)。文言・項目・理由は消す。</summary>
    public static SettingsScreen Reset(SettingsScreen screen) =>
        screen with { Values = PracticeChoiceApplier.Defaults, Message = null, Issues = [], Diagnostic = null };

    /// <summary>保存する。成功なら null(今の設定を置き換えた)。失敗なら設定画面(入力した値と理由)を返す。</summary>
    public SettingsScreen? Save(SettingsScreen screen, PracticeChoice values)
    {
        var updated = PracticeChoiceApplier.Apply(Current, values);
        var result = repository!.Save(updated);
        switch (result.Status)
        {
            case SettingsSaveStatus.Saved:
                Current = updated;
                return null;
            case SettingsSaveStatus.Invalid:
                return screen with { Values = values, Message = InvalidMessage, Issues = [.. result.Issues], Diagnostic = result.Reason };
            default:
                return screen with { Values = values, Message = SaveFailedMessage, Issues = [], Diagnostic = result.Reason };
        }
    }

    private static string CannotSaveReason(LoadStatus? status) => status switch
    {
        LoadStatus.NewerVersion => "新しい版のアプリで作られた設定のため、保存できません。画面の値は初期設定です。",
        _ => "設定ファイルを読めないため、保存できません。画面の値は初期設定です。アプリを再起動すると保存できることがあります。",
    };
}
