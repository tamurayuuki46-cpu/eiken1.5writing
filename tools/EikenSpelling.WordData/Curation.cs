namespace EikenSpelling.WordData;

/// <summary>
/// Claude が書く付加データ(word-data design 2章)。どれも UTF-8 のテキストで、`#` で始まる行と空行は無視する。
/// - 訳: `word&lt;TAB&gt;訳`(data/curation/ja/*.tsv)
/// - Tier 1: 1行1語(data/curation/tier1.txt)
/// - 除外: `word&lt;TAB&gt;理由`(data/curation/excluded.tsv)
/// - 手動の穴あき: `word&lt;TAB&gt;穴あき`(data/curation/blanks-manual.tsv)
/// </summary>
internal sealed record Curation(
    IReadOnlyDictionary<string, string> Translations,
    IReadOnlySet<string> Tier1,
    IReadOnlyDictionary<string, string> Excluded,
    IReadOnlyDictionary<string, string> ManualBlanks)
{
    public static readonly Curation Empty = new(
        new Dictionary<string, string>(), new HashSet<string>(), new Dictionary<string, string>(), new Dictionary<string, string>());

    /// <summary>`word&lt;TAB&gt;値` の行を読む。同じ語が2回あれば誤り(どちらが正しいか決められないため)。</summary>
    public static Dictionary<string, string> ReadPairs(IEnumerable<(string Name, string Text)> files)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, text) in files)
        {
            var lineNo = 0;
            foreach (var raw in text.TrimStart('﻿').Split('\n'))
            {
                lineNo++;
                var line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                var tab = line.IndexOf('\t');
                if (tab <= 0)
                {
                    throw new InvalidDataException($"{name} の {lineNo} 行目にタブがありません。");
                }

                var word = line[..tab].Trim().ToLowerInvariant();
                var value = line[(tab + 1)..].Trim();
                if (value.Length == 0)
                {
                    throw new InvalidDataException($"{name} の {lineNo} 行目(「{word}」)の値が空です。");
                }

                if (!map.TryAdd(word, value))
                {
                    throw new InvalidDataException($"{name} の {lineNo} 行目: 「{word}」が2回あります。");
                }
            }
        }

        return map;
    }

    public static HashSet<string> ReadWords(string text) =>
        [.. text.TrimStart('﻿').Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.ToLowerInvariant())];

    /// <summary>data/curation/ を読む。無いファイルは空として扱う(作業の途中でも生成できるよう)。</summary>
    public static Curation Load(string curationDir)
    {
        static string? Read(string path) => File.Exists(path) ? SourceLists.ReadUtf8(path) : null;

        var jaDir = Path.Combine(curationDir, "ja");
        var jaFiles = Directory.Exists(jaDir)
            ? Directory.GetFiles(jaDir, "*.tsv").Order(StringComparer.Ordinal).Select(p => (Path.GetFileName(p), SourceLists.ReadUtf8(p)))
            : [];
        return new Curation(
            ReadPairs(jaFiles),
            Read(Path.Combine(curationDir, "tier1.txt")) is { } tier1 ? ReadWords(tier1) : new HashSet<string>(),
            Read(Path.Combine(curationDir, "excluded.tsv")) is { } ex ? ReadPairs([("excluded.tsv", ex)]) : new Dictionary<string, string>(),
            Read(Path.Combine(curationDir, "blanks-manual.tsv")) is { } bl ? ReadPairs([("blanks-manual.tsv", bl)]) : new Dictionary<string, string>());
    }
}
