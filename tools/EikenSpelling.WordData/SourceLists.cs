using System.Text;

namespace EikenSpelling.WordData;

/// <summary>元リストの1語。Rank はそのリストの順位(無ければ null)。</summary>
internal sealed record SourceWord(string Word, int? Rank);

/// <summary>
/// 公式 CSV の読込(data/sources/SOURCES.md)。列名で読み、余分な列は無視する。ファイルは改変しない。
/// 見出し語だけを使い、変化形(2列目以降)は使わない。
/// </summary>
internal static class SourceLists
{
    public const string Ngsl = "NGSL";
    public const string Nawl = "NAWL";
    public const string Bsl = "BSL";

    /// <summary>NGSL 1.2 stats: `Lemma,SFI Rank,...`。</summary>
    public static IReadOnlyList<SourceWord> ReadNgslStats(string text) => ReadWithHeader(text, "Lemma", "SFI Rank");

    /// <summary>BSL 1.2 stats: `Word,BSL Rank,...`(空の列があっても読む)。</summary>
    public static IReadOnlyList<SourceWord> ReadBslStats(string text) => ReadWithHeader(text, "Word", "BSL Rank");

    /// <summary>見出しの無い lemma の CSV(NAWL の研究用・NGSL の補足)。1列目が見出し語。順位なし。</summary>
    public static IReadOnlyList<SourceWord> ReadLemmatized(string text) =>
        [.. Lines(text).Select(l => l.Split(',')[0].Trim()).Where(w => w.Length > 0).Select(w => new SourceWord(w, null))];

    /// <summary>NAWL の研究用 CSV は ISO-8859-1(SOURCES.md)。</summary>
    public static string ReadLatin1(string path) => File.ReadAllText(path, Encoding.Latin1);

    public static string ReadUtf8(string path) => File.ReadAllText(path, new UTF8Encoding(false));

    private static IReadOnlyList<SourceWord> ReadWithHeader(string text, string wordColumn, string rankColumn)
    {
        var lines = Lines(text).ToList();
        if (lines.Count == 0)
        {
            throw new InvalidDataException("CSV が空です。");
        }

        var header = lines[0].Split(',').Select(h => h.Trim()).ToList();
        var wordAt = header.IndexOf(wordColumn);
        var rankAt = header.IndexOf(rankColumn);
        if (wordAt < 0 || rankAt < 0)
        {
            throw new InvalidDataException($"列「{wordColumn}」「{rankColumn}」が見出しにありません。");
        }

        var words = new List<SourceWord>();
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');
            var word = cells.ElementAtOrDefault(wordAt)?.Trim() ?? "";
            if (word.Length == 0)
            {
                continue;
            }

            if (!int.TryParse(cells.ElementAtOrDefault(rankAt), out var rank))
            {
                throw new InvalidDataException($"「{word}」の順位が読めません。");
            }

            words.Add(new SourceWord(word, rank));
        }

        return words;
    }

    /// <summary>行に分ける(BOM・CR を除き、空行は捨てる)。</summary>
    private static IEnumerable<string> Lines(string text) =>
        text.TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0);
}
