using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

/// <summary>Small popup: "you have N topics / M cards to review" - shown once right as the study window
/// opens (at wake-up, or at the end of school on a school day), never during sleep or school hours.</summary>
public class WakeNudgePopup : Window
{
    public WakeNudgePopup(Window owner, int dueTopics, int dueCards, Action goStudy, Action goCards)
    {
        UI.Setup(this, owner);
        Title = Loc.Get("wake_nudge_title");
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var sp = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        UI.BindFlow(sp);

        var badge = new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(28),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12)
        };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var glyph = new TextBlock { Text = "\uE7BE", FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
        badge.Child = glyph;
        sp.Children.Add(badge);

        sp.Children.Add(new TextBlock
        {
            Text = Loc.Get("wake_nudge_title"),
            FontSize = 18, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap
        });

        string msg = dueTopics > 0 && dueCards > 0
            ? string.Format(Loc.Get("wake_nudge_both"), dueTopics, dueCards)
            : dueTopics > 0
                ? string.Format(Loc.Get("wake_nudge_topics"), dueTopics)
                : string.Format(Loc.Get("wake_nudge_cards"), dueCards);
        var msgBlock = new TextBlock { Text = msg, FontSize = 14, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
        msgBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        sp.Children.Add(msgBlock);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        if (dueTopics > 0)
            row.Children.Add(UI.Btn("nav_study", (s, e) => { goStudy?.Invoke(); Close(); }));
        if (dueCards > 0)
            row.Children.Add(UI.Btn("nav_cards", (s, e) => { goCards?.Invoke(); Close(); }));
        row.Children.Add(UI.Btn("ok", (s, e) => Close(), true));
        sp.Children.Add(row);

        Content = sp;
    }
}
