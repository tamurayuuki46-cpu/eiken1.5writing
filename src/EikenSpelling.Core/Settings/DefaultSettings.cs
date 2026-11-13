namespace EikenSpelling.Core.Settings;

/// <summary>v1.1 の決定値を初期値とする設定。初期値の定義は LearningSettings の各プロパティに一本化している。</summary>
public static class DefaultSettings
{
    public static LearningSettings Create() => new();
}
