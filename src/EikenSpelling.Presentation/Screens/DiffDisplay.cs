using EikenSpelling.Core.Domain;

namespace EikenSpelling.Presentation.Screens;

/// <summary>1文字の表示。Marked は強調(正しいスペル側)または取り消し線(入力側)。</summary>
public sealed record DiffLetter(char Character, bool Marked)
{
    /// <summary>入力側の文字は誤答入力なので伏せる(logging.md)。</summary>
    public override string ToString() => $"DiffLetter {{ Marked = {Marked}, Character = (伏せる) }}";
}

/// <summary>
/// 判定の表示用データ(design 4.5)。文章は Core の SpellingDiffService.Describe を使い、ここでは作らない。
/// 入力は正規化した文字列(Diff の InputPosition はその位置)。ログ・ToString に入力・文章を出さない(class にして ToString を伏せる)。
/// </summary>
public sealed class DiffDisplay
{
    /// <summary>文章を出す最大の件数。残りは「ほか n か所」にまとめる(UI 設計 5章)。</summary>
    public const int MaxMessages = 3;

    public DiffDisplay(string correctWord, string normalizedInput, IReadOnlyList<SpellingDiff> diffs)
    {
        var marked = diffs.Where(d => d.Kind != DiffKind.Extra).Select(d => d.CorrectPosition).ToHashSet();
        var struck = diffs.Where(d => d.Kind == DiffKind.Extra).Select(d => d.InputPosition).ToHashSet();
        CorrectLetters = [.. correctWord.Select((c, i) => new DiffLetter(c, marked.Contains(i + 1)))];
        InputText = normalizedInput;
        InputLetters = [.. normalizedInput.Select((c, i) => new DiffLetter(c, struck.Contains(i + 1)))];
        Messages = [.. diffs.Take(MaxMessages).Select(SpellingDiffService.Describe)];
        MoreText = diffs.Count > MaxMessages ? $"ほか {diffs.Count - MaxMessages} か所" : null;
    }

    public IReadOnlyList<DiffLetter> CorrectLetters { get; }

    public IReadOnlyList<DiffLetter> InputLetters { get; }

    /// <summary>あなたの入力(正規化した文字列)。画面にだけ出す。</summary>
    public string InputText { get; }

    public IReadOnlyList<string> Messages { get; }

    public string? MoreText { get; }

    public override string ToString() => $"DiffDisplay {{ Messages = {Messages.Count}, Input = (伏せる) }}";
}
