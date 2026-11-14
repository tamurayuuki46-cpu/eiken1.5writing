using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EikenSpelling.Wpf;

/// <summary>null・空文字・空の一覧なら Collapsed。</summary>
public sealed class NotNullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => Visibility.Collapsed,
        string s when s.Length == 0 => Visibility.Collapsed,
        ICollection c when c.Count == 0 => Visibility.Collapsed,
        _ => Visibility.Visible,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>空の一覧なら Visible(「なし」の表示用)。</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ICollection { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>偽なら Visible。</summary>
public sealed class InverseBoolToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// 正しいスペルの文字の大きさを、単語の長さで段階的に下げる(UI 設計 5章: 52 → 40 → 32。Viewbox は使わない)。
/// 骨組みでは長さで近似する(内容の最大幅 760 で、等幅の太字 52 は約 12 文字まで収まる)。
/// </summary>
public sealed class WordFontSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var length = value is int n ? n : 0;
        return length <= 12 ? 52.0 : length <= 16 ? 40.0 : 32.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
