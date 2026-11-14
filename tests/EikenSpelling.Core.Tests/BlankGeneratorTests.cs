using EikenSpelling.Core.Domain;

namespace EikenSpelling.Core.Tests;

/// <summary>穴あきの自動生成(仕様 v1.1 5章、word-data design 4.3)。</summary>
public class BlankGeneratorTests
{
    [Theory]
    [InlineData("committee", "co__i__ee", BlankRule.DoubleConsonant)] // 二重子音は2か所まで
    [InlineData("necessary", "nece__ary", BlankRule.DoubleConsonant)]
    [InlineData("mallett", "ma__ett", BlankRule.DoubleConsonant)] // 2か所目を隠すと半分(3文字)を超えるので1か所だけ
    [InlineData("possible", "po___ble", BlankRule.DoubleConsonant)] // 二重子音に加えて -ible の母音も隠す(上限の範囲で)
    [InlineData("accessible", "a__e___ble", BlankRule.DoubleConsonant)]
    [InlineData("ally", "a__y", BlankRule.DoubleConsonant)] // 4文字の二重子音
    [InlineData("class", "cla__", BlankRule.DoubleConsonant)] // 末尾の二重子音
    [InlineData("mississippi", "mi__i__ippi", BlankRule.DoubleConsonant)] // 3か所あっても2か所まで
    [InlineData("argument", "arg_m_nt", BlankRule.MiddleVowel)] // -ment は -ent の規則にしない
    [InlineData("development", "dev_l_pment", BlankRule.MiddleVowel)]
    [InlineData("egg", "e_g", BlankRule.Short)]
    [InlineData("movie", "mov__", BlankRule.IeEi)] // ie が末尾
    [InlineData("comfortable", "comfort_ble", BlankRule.Suffix)] // -able
    [InlineData("sensible", "sens_ble", BlankRule.Suffix)] // -ible
    [InlineData("performance", "perform_nce", BlankRule.Suffix)] // -ance
    [InlineData("existence", "exist_nce", BlankRule.Suffix)] // -ence
    [InlineData("independent", "independ_nt", BlankRule.Suffix)] // -ent(6文字以上)
    [InlineData("receive", "rec__ve", BlankRule.IeEi)]
    [InlineData("believe", "bel__ve", BlankRule.IeEi)]
    [InlineData("know", "_now", BlankRule.SilentLetter)]
    [InlineData("write", "_rite", BlankRule.SilentLetter)]
    [InlineData("climb", "clim_", BlankRule.SilentLetter)]
    [InlineData("thought", "thou__t", BlankRule.SilentLetter)]
    [InlineData("problem", "pr_blem", BlankRule.MiddleVowel)] // 7文字以下は母音1つ
    [InlineData("education", "ed_c_tion", BlankRule.MiddleVowel)] // 8文字以上は中ほどの母音2つ
    [InlineData("tax", "t_x", BlankRule.Short)]
    [InlineData("law", "l_w", BlankRule.Short)]
    [InlineData("go", "g_", BlankRule.Short)]
    [InlineData("rhythm", "rh_thm", BlankRule.MiddleVowel)] // y も母音として扱う
    [InlineData("strengths", "str_ngths", BlankRule.MiddleVowel)] // 母音が1つしか無ければ1つ
    [InlineData("tsktsk", "ts_tsk", BlankRule.Fallback)] // 母音が無ければ中ほどの文字
    public void Rules_produce_the_expected_blanks(string word, string blanks, BlankRule rule)
    {
        var generated = BlankGenerator.Generate(word);

        Assert.Equal(blanks, generated.Blanks);
        Assert.Equal(rule, generated.Rule);
    }

    [Theory]
    [InlineData("want")] // -ant でも 6 文字未満は接尾辞の規則にしない
    [InlineData("went")]
    [InlineData("table")] // -able でも 6 文字未満は接尾辞の規則にしない
    public void Short_words_ending_like_suffixes_do_not_use_the_suffix_rule(string word) =>
        Assert.NotEqual(BlankRule.Suffix, BlankGenerator.Generate(word).Rule);

    [Theory]
    [InlineData("accommodate")]
    [InlineData("embarrass")]
    [InlineData("possession")]
    [InlineData("assessment")]
    [InlineData("go")]
    [InlineData("tsktsk")]
    [InlineData("mississippi")]
    [InlineData("bookkeeper")]
    [InlineData("queue")]
    public void Generated_blanks_always_pass_validation_and_hide_at_most_half(string word)
    {
        var blanks = BlankGenerator.Generate(word).Blanks;

        Assert.Null(WordSetValidator.ValidateBlanks(word, blanks));
        var hidden = blanks.Count(c => c == WordSetValidator.BlankChar);
        Assert.InRange(hidden, 1, Math.Max(1, word.Length / 2));
    }

    [Fact]
    public void Generation_is_deterministic() =>
        Assert.Equal(BlankGenerator.Generate("government"), BlankGenerator.Generate("government"));

    [Fact]
    public void Uppercase_input_keeps_its_letters()
    {
        var blanks = BlankGenerator.Generate("Committee").Blanks;

        Assert.Equal("Co__i__ee", blanks);
        Assert.Null(WordSetValidator.ValidateBlanks("Committee", blanks));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")] // 1文字は全部を隠さずに穴を作れない
    [InlineData("well-known")]
    [InlineData("o'clock")]
    [InlineData("ice cream")]
    [InlineData("café")] // 英字以外(アクセント付き)
    public void Words_with_non_letters_or_empty_are_rejected(string word) =>
        Assert.Throws<ArgumentException>(() => BlankGenerator.Generate(word));
}
