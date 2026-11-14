using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EikenSpelling.Wpf;

/// <summary>
/// フォーカスの輪(R20「フォーカスの位置が常に見える」、UI 設計 4章)。
/// WPF の FocusVisualStyle はキーボード操作の後にしか描かれず、画面が出た直後の初期フォーカスに輪が出ない(実機確認 B2)。
/// そこで、Show を真にした部品がキーボードフォーカスを持つ間は、入力の種類にかかわらず装飾(Adorner)で輪を描く。
/// 使う側のスタイルでは FocusVisualStyle を null にして二重の輪を防ぐ。
/// </summary>
public static class FocusRing
{
    public static readonly DependencyProperty ShowProperty = DependencyProperty.RegisterAttached(
        "Show", typeof(bool), typeof(FocusRing), new PropertyMetadata(false, OnShowChanged));

    private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached(
        "Adorner", typeof(RingAdorner), typeof(FocusRing));

    public static bool GetShow(DependencyObject element) => (bool)element.GetValue(ShowProperty);

    public static void SetShow(DependencyObject element, bool value) => element.SetValue(ShowProperty, value);

    private static void OnShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.GotKeyboardFocus -= OnGotFocus;
        element.LostKeyboardFocus -= OnLostFocus;
        element.Unloaded -= OnUnloaded;
        Remove(element);
        if ((bool)e.NewValue)
        {
            element.GotKeyboardFocus += OnGotFocus;
            element.LostKeyboardFocus += OnLostFocus;
            element.Unloaded += OnUnloaded;
            if (element.IsKeyboardFocused)
            {
                Add(element);
            }
        }
    }

    // 子(ComboBox の中の部品など)のフォーカスも通るので、部品そのものが持つときだけ描く
    private static void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsKeyboardFocused)
        {
            Add(element);
        }
    }

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && !element.IsKeyboardFocused)
        {
            Remove(element);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) => Remove((FrameworkElement)sender);

    private static void Add(FrameworkElement element)
    {
        if (element.GetValue(AdornerProperty) is not null)
        {
            return;
        }

        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null)
        {
            return;
        }

        var brush = element.TryFindResource("Accent") as Brush ?? SystemColors.HighlightBrush;
        var adorner = new RingAdorner(element, brush);
        layer.Add(adorner);
        element.SetValue(AdornerProperty, adorner);
    }

    private static void Remove(FrameworkElement element)
    {
        if (element.GetValue(AdornerProperty) is not RingAdorner adorner)
        {
            return;
        }

        element.ClearValue(AdornerProperty);
        AdornerLayer.GetAdornerLayer(element)?.Remove(adorner);
    }

    /// <summary>部品の外側 5px に角丸 14・太さ 2 の輪(旧 FocusRing スタイルと同じ見た目)。</summary>
    private sealed class RingAdorner : Adorner
    {
        private const double Gap = 5;
        private const double Radius = 14;
        private readonly Pen _pen;

        public RingAdorner(UIElement adorned, Brush brush)
            : base(adorned)
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
            _pen = new Pen(brush, 2);
            _pen.Freeze();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var size = AdornedElement.RenderSize;
            var rect = new Rect(-Gap, -Gap, size.Width + (Gap * 2), size.Height + (Gap * 2));
            drawingContext.DrawRoundedRectangle(null, _pen, rect, Radius, Radius);
        }
    }
}
