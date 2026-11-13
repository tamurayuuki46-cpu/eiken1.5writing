using System.Text.Json.Serialization;

namespace EikenSpelling.Core.Models;

/// <summary>
/// 単語の学習状態(v1.0 7.2)。「未出題」は記録が無いことで表し、「再確認待ち」は
/// 克服済みかつ予定日が今日以前であることから求める(保存しない)。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LearningState>))]
public enum LearningState
{
    /// <summary>出題済みで苦手登録されていない。</summary>
    Normal,

    /// <summary>苦手。</summary>
    Weak,

    /// <summary>克服済み(定期再確認の対象)。</summary>
    Mastered,
}
