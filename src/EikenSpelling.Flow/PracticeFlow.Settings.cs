namespace EikenSpelling.Flow;

/// <summary>設定画面の操作(practice-settings design 4.3)。設定の中身は SettingsEditor が扱う。</summary>
public sealed partial class PracticeFlow
{
    /// <summary>ホームから設定画面へ。何も書かない。</summary>
    public void OpenSettings()
    {
        Require<HomeScreen>();
        Screen = _editor.Open(_options.AvailableModes);
    }

    /// <summary>基本設定を保存する(保存できるときだけ)。成功ならホームへ、失敗なら設定画面のまま理由を出す。</summary>
    public void SaveSettings(PracticeChoice values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var screen = Require<SettingsScreen>(s => s.CanSave);
        Screen = (FlowScreen?)_editor.Save(screen, values) ?? Home(message: "設定を保存しました。");
    }

    /// <summary>画面の値を基本設定の初期値にする。何も書かない(保存は SaveSettings)。</summary>
    public void ResetSettings() => Screen = SettingsEditor.Reset(Require<SettingsScreen>());

    /// <summary>保存せずにホームへ。何も書かない。</summary>
    public void CloseSettings()
    {
        Require<SettingsScreen>();
        Screen = Home();
    }
}
