using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

// Prayer times tab: six times per day, filled from a CSV file chosen by the user (the table starts empty)
public partial class MainWindow
{
    static string FormatLeft(TimeSpan d)
    {
        int total = Math.Max(0, (int)Math.Ceiling(d.TotalMinutes));
        int h = total / 60, m = total % 60;
        return h > 0 ? string.Format(Loc.Get("pr_left_hm"), h, m) : string.Format(Loc.Get("pr_left_m"), m);
    }

    static TextBlock Muted(string key, double size = 12.5)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size, Margin = new Thickness(0, 2, 0, 8) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(t, TextBlock.TextProperty, key);
        return t;
    }

    static TextBlock Ltr(string text, double size, bool bold)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FlowDirection = System.Windows.FlowDirection.LeftToRight,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    void BuildPrayer()
    {
        if (PrayerHost == null) return;
        var h = PrayerHost;
        h.Children.Clear();

        var range = Db.PrayRange();
        if (range.count == 0)
        {
            BuildPrayerEmpty(h);
            return;
        }

        var now = DateTime.Now;
        var today = Db.GetPrayDay(now.Date);
        var next = PrayTimes.Next(now);

        // ---- card 1: today
        var card = NewCard(h, "pr_today_title", "\uE708");
        card.Children.Add(new TextBlock
        {
            Text = Loc.Get("d_" + (int)now.DayOfWeek) + "   (" + ReviewEngine.Iso(now) + ")",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 4)
        });

        if (next != null)
        {
            var n = next.Value;
            string name = Loc.Get(PrayTimes.Keys[n.index]);
            string day = n.when.Date == now.Date ? "" : n.when.Date == now.Date.AddDays(1) ? " (" + Loc.Get("tt_tomorrow") + ")" : "";
            var line = new TextBlock
            {
                Text = string.Format(Loc.Get("pr_next"), name + day, n.when.ToString("HH:mm", CultureInfo.InvariantCulture), FormatLeft(n.when - now)),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            line.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            card.Children.Add(line);
        }

        if (today == null)
        {
            card.Children.Add(Muted("pr_no_today", 13.5));
        }
        else
        {
            for (int i = 0; i < 6; i++)
            {
                bool isNext = next != null && next.Value.index == i && next.Value.when == SafeAt(now.Date, today, i);
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock { FontSize = 13.5, FontWeight = isNext ? FontWeights.Bold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
                UI.Bind(name, TextBlock.TextProperty, PrayTimes.Keys[i]);
                var time = Ltr(today[i], 14.5, true);
                Grid.SetColumn(time, 1);
                row.Children.Add(name);
                row.Children.Add(time);

                var box = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 2, 0, 2), Child = row };
                box.SetResourceReference(Border.BackgroundProperty, isNext ? "AccentBrush" : "InputBrush");
                if (isNext)
                {
                    name.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
                    time.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
                }
                card.Children.Add(box);
            }
        }

        // ---- card 2: the coming days
        var upcoming = new List<(DateTime day, string[] t)>();
        for (int off = 1; off <= 7; off++)
        {
            var d = now.Date.AddDays(off);
            var t = Db.GetPrayDay(d);
            if (t != null) upcoming.Add((d, t));
        }
        if (upcoming.Count > 0)
        {
            var up = NewCard(h, "pr_upcoming", "\uE787");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 6; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 52 });
            for (int r = 0; r <= upcoming.Count; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < 6; i++)
            {
                var head = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 6) };
                UI.Bind(head, TextBlock.TextProperty, PrayTimes.Keys[i]);
                Grid.SetColumn(head, i + 1);
                grid.Children.Add(head);
            }
            for (int r = 0; r < upcoming.Count; r++)
            {
                var (d, t) = upcoming[r];
                var label = new StackPanel { Margin = new Thickness(0, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center };
                var dn = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 12.5 };
                UI.Bind(dn, TextBlock.TextProperty, "d_" + (int)d.DayOfWeek);
                var dd = Ltr(ReviewEngine.Iso(d), 10.5, false);
                dd.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
                label.Children.Add(dn);
                label.Children.Add(dd);
                Grid.SetRow(label, r + 1);
                grid.Children.Add(label);
                for (int i = 0; i < 6; i++)
                {
                    var tb = Ltr(t[i], 12.5, false);
                    tb.HorizontalAlignment = HorizontalAlignment.Center;
                    Grid.SetRow(tb, r + 1);
                    Grid.SetColumn(tb, i + 1);
                    grid.Children.Add(tb);
                }
            }
            up.Children.Add(grid);
        }

        // ---- alarms: which of the six times ring
        var al = NewCard(h, "pr_alarms", "\uE7ED");
        var alWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            var cb = new CheckBox { IsChecked = Cfg.PrayAlarm(idx), Margin = new Thickness(0, 4, 18, 4) };
            UI.Bind(cb, ContentControl.ContentProperty, PrayTimes.Keys[idx]);
            cb.Click += (s2, e2) => Cfg.SetPrayAlarm(idx, cb.IsChecked == true);
            alWrap.Children.Add(cb);
        }
        al.Children.Add(alWrap);

        // ---- card 3: the data (range, import, clear)
        var data = NewCard(h, "pr_data", "\uE8B7");
        data.Children.Add(new TextBlock
        {
            Text = string.Format(Loc.Get("pr_range"), Jal.FromIso(range.first), Jal.FromIso(range.last), range.count),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 6)
        });
        if (string.CompareOrdinal(range.last, ReviewEngine.IsoG(now.Date.AddDays(7))) < 0)
        {
            var warn = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            warn.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            UI.Bind(warn, TextBlock.TextProperty, "pr_expiring");
            data.Children.Add(warn);
        }
        data.Children.Add(PrayerButtons(true));
    }

    static DateTime SafeAt(DateTime day, string[] t, int i)
    {
        try { return PrayTimes.At(day, t, i); } catch { return DateTime.MinValue; }
    }

    void BuildPrayerEmpty(StackPanel h)
    {
        var card = NewCard(h, "pr_title", "\uE708");
        var empty = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 4, 0, 10) };
        UI.Bind(empty, TextBlock.TextProperty, "pr_empty");
        card.Children.Add(empty);
        card.Children.Add(PrayerButtons(false));
    }

    StackPanel PrayerButtons(bool canClear)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(UI.Btn("pr_import", PrayImport_Click));
        if (canClear)
        {
            var clear = UI.Btn("pr_clear", PrayClear_Click, true);
            clear.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(clear);
        }
        return row;
    }

    void PrayImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CSV (*.csv)|*.csv|" + Loc.Get("all_files") + " (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;

        var (imported, skipped, error) = PrayTimes.Import(dlg.FileName);
        if (error != null) { Dialogs.Info(this, error); return; }
        BuildPrayer();
        Dialogs.Info(this, string.Format(Loc.Get("pr_imported"), imported, skipped));
    }

    void PrayClear_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.Confirm(this, Loc.Get("pr_clear_confirm"))) return;
        Db.ClearPrayTimes();
        BuildPrayer();
    }
}
