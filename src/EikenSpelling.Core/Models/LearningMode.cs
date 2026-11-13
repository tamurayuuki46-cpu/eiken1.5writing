using System.Text.Json.Serialization;

namespace EikenSpelling.Core.Models;

/// <summary>
/// 出題モード。並び順は端数配分で同点のときの優先順位(日本語 → 音声 → 穴あき → フラッシュ)を兼ねる。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LearningMode>))]
public enum LearningMode
{
    Japanese,
    Audio,
    Blank,
    Flash,
}
