using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Wpf.Views;

/// <summary>
/// 設定。先頭の項目(1日の目標)にフォーカスを置く(UX 設計の設定の行)。
/// 保存・検査に失敗して作り直された画面では、警告の文をナレーターに知らせる(LiveSetting だけでは読まれない)。
/// </summary>
public partial class SettingsView : UserControl, IInitialFocus
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => LiveRegion.Announce(MessageText, (DataContext as SettingsViewModel)?.Message is not null);
    }

    public void FocusInitial() => GoalBox.Focus();
}

/// <summary>LiveSetting を付けた文の変化をスクリーンリーダーへ知らせる(WPF は自動では知らせない)。</summary>
internal static class LiveRegion
{
    public static void Announce(UIElement element, bool when)
    {
        if (when)
        {
            UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
