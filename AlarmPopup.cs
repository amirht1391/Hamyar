using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

/// <summary>Window that appears when an alarm rings.</summary>
public class AlarmPopup : Window
{
    readonly Action<int> _snooze;

    public AlarmPopup(Item it, Action<int> onSnooze, bool noSnooze = false)
    {
        _snooze = onSnooze;
        Closed += (s, e) => AudioService.Stop();   // the X button must silence the alarm too
        UI.Setup(this, null);
        Title = Loc.Get("alarm_ringing");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var sp = new StackPanel { Margin = new Thickness(26, 24, 26, 22), HorizontalAlignment = HorizontalAlignment.Stretch };
        UI.BindFlow(sp);

        var badge = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14)
        };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var glyph = new TextBlock { Text = "\uE7ED", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
        badge.Child = glyph;
        sp.Children.Add(badge);

        sp.Children.Add(new TextBlock
        {
            Text = it.Title, FontSize = 22, FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center
        });
        // live system clock (like a phone's ringing screen), not the fixed alarm time
        var clock = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), FontSize = 40, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), FlowDirection = FlowDirection.LeftToRight
        };
        var ticker = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        ticker.Tick += (s, e) => clock.Text = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        ticker.Start();
        Closed += (s, e) => ticker.Stop();
        sp.Children.Add(clock);
        sp.Children.Add(new TextBlock
        {
            Text = Jal.Date(DateTime.Today) + "   ·   ⏰ " + it.TimeText, FontSize = 13, Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.LeftToRight
        });
        if (!string.IsNullOrWhiteSpace(it.Notes))
            sp.Children.Add(new TextBlock { Text = it.Notes, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
        var stop = UI.Btn("stop", (s, e) => StopNow());
        stop.MinWidth = noSnooze ? 180 : 120;
        var snooze = UI.Btn("snooze", null, true);
        snooze.MinWidth = 120;
        snooze.Click += (s, e) =>
        {
            var menu = new ContextMenu { PlacementTarget = snooze, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            menu.SetResourceReference(Control.FontFamilyProperty, "AppFont");
            foreach (var m in new[] { 5, 10, 15, 30, 60 })
            {
                int mm = m;
                var mi = new MenuItem { Header = "+" + (m < 60 ? m + " min" : "1 h") };
                mi.Click += (s2, e2) => SnoozeNow(mm);
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        };
        row.Children.Add(stop);
        if (!noSnooze) row.Children.Add(snooze);
        sp.Children.Add(row);
        Content = sp;
    }

    public void StopNow()
    {
        AudioService.Stop();
        Close();
    }

    public void SnoozeNow(int minutes = 5)
    {
        AudioService.Stop();
        _snooze?.Invoke(minutes);
        Close();
    }
}
