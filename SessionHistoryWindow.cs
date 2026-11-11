using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;

namespace Hamyar;

/// <summary>Totals, a 7-day chart and every past study session. A session can be opened to read / edit its notes and see its board.</summary>
public class SessionHistoryWindow : Window
{
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    public SessionHistoryWindow(Window owner)
    {
        UI.Setup(this, owner, true);
        Title = Loc.Get("fs_hist_title");
        Width = 780;
        Height = 780;
        MinHeight = 520;
        Content = _scroll;
        Build();
    }

    TextBlock T(string text, double size = 14, bool bold = false, bool sub = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, sub ? "SubFgBrush" : "FgBrush");
        return t;
    }

    Border Card(UIElement child, Thickness margin)
    {
        var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 10), Margin = margin, Child = child, BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        return b;
    }

    void Build()
    {
        var sessions = Db.GetSessions();
        var root = new StackPanel { Margin = new Thickness(20) };
        UI.BindFlow(root);

        // ---- totals
        int today = sessions.Where(s => s.StartedAt.Date == DateTime.Today).Sum(s => s.Seconds);
        int week = sessions.Where(s => s.StartedAt.Date >= DateTime.Today.AddDays(-6)).Sum(s => s.Seconds);
        int all = sessions.Sum(s => s.Seconds);
        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        var items = new[] { ("fs_today", today), ("fs_week", week), ("fs_all_time", all) };
        for (int i = 0; i < items.Length; i++)
        {
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var sp = new StackPanel();
            sp.Children.Add(T(Loc.Get(items[i].Item1), 12.5, false, true));
            sp.Children.Add(T(FocusUtil.Dur(items[i].Item2), 22, true));
            var c = Card(sp, new Thickness(i == 0 ? 0 : 6, 0, i == items.Length - 1 ? 0 : 6, 0));
            Grid.SetColumn(c, i);
            stats.Children.Add(c);
        }
        root.Children.Add(stats);

        // ---- 7-day chart
        var chart = new Grid { Height = 150 };
        int max = 1;
        var perDay = new int[7];
        for (int i = 0; i < 7; i++)
        {
            var d = DateTime.Today.AddDays(i - 6);
            perDay[i] = sessions.Where(s => s.StartedAt.Date == d).Sum(s => s.Seconds);
            max = Math.Max(max, perDay[i]);
        }
        for (int i = 0; i < 7; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var col = new Grid { Margin = new Thickness(4, 0, 4, 0) };
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var bar = new Border { VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(6), Height = Math.Max(3, 100.0 * perDay[i] / max), Margin = new Thickness(6, 0, 6, 4) };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            bar.ToolTip = FocusUtil.Dur(perDay[i]);
            var lbl = T(Jal.Date(DateTime.Today.AddDays(i - 6)).Substring(5), 11, false, true);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            lbl.FlowDirection = FlowDirection.LeftToRight;
            Grid.SetRow(lbl, 1);
            col.Children.Add(bar);
            col.Children.Add(lbl);
            Grid.SetColumn(col, i);
            chart.Children.Add(col);
        }
        root.Children.Add(Card(chart, new Thickness(0, 0, 0, 14)));

        // ---- sessions
        if (sessions.Count == 0)
        {
            var empty = T(Loc.Get("fs_hist_empty"), 15, false, true);
            empty.Margin = new Thickness(4, 20, 0, 0);
            root.Children.Add(empty);
        }
        foreach (var s in sessions)
        {
            var sess = s;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var body = new StackPanel();
            body.Children.Add(T(sess.Title, 16, true));
            var (done, total) = sess.TaskCounts();
            var parts = new List<string> { sess.DateText, sess.DurationText };
            if (sess.PlannedSeconds > 0) parts.Add(string.Format(Loc.Get("fs_planned"), FocusUtil.Dur(sess.PlannedSeconds)));
            if (total > 0) parts.Add(string.Format(Loc.Get("fs_tasks_n"), done, total));
            if (sess.Cards > 0) parts.Add(string.Format(Loc.Get("fs_cards_n"), sess.Cards));
            if (sess.HasInk) parts.Add("✎ " + Loc.Get("fs_board"));
            if (sess.Rating > 0) parts.Add(new string('★', sess.Rating));
            body.Children.Add(T(string.Join("  ·  ", parts), 12.5, false, true));
            var preview = (sess.Notes ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (preview.Length > 0)
            {
                var pv = T(preview.Length > 160 ? preview.Substring(0, 160) + "…" : preview, 13, false, true);
                pv.FontStyle = FontStyles.Italic;
                pv.Margin = new Thickness(0, 3, 0, 0);
                body.Children.Add(pv);
            }
            g.Children.Add(body);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(UI.IconBtn("\uE8A7", (a, b) => { new SessionDetailDialog(this, sess).ShowDialog(); Build(); }, "fs_open"));
            actions.Children.Add(UI.IconBtn("\uE74D", (a, b) =>
            {
                if (!Dialogs.Confirm(this, Loc.Get("fs_confirm_del_session"))) return;
                Db.DeleteSession(sess.Id);
                Build();
            }, "delete"));
            Grid.SetColumn(actions, 1);
            g.Children.Add(actions);
            root.Children.Add(Card(g, new Thickness(0, 0, 0, 8)));
        }

        _scroll.Content = root;
    }
}

/// <summary>One past session: edit its notes, see its checklist and the board that was drawn.</summary>
public class SessionDetailDialog : Window
{
    readonly StudySession _s;
    readonly TextBox _notes = new();

    public SessionDetailDialog(Window owner, StudySession s)
    {
        _s = s;
        UI.Setup(this, owner, true);
        Title = s.Title;
        Width = 700;
        Height = 780;
        MinHeight = 520;

        var root = new StackPanel { Margin = new Thickness(20) };
        UI.BindFlow(root);
        root.Children.Add(new TextBlock { Text = s.Title, FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        var info = new TextBlock { Text = s.DateText + "  ·  " + s.DurationText, Margin = new Thickness(0, 2, 0, 0) };
        info.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        root.Children.Add(info);

        root.Children.Add(UI.Lbl("fs_notes"));
        _notes.Text = s.Notes;
        _notes.AcceptsReturn = true;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.MinHeight = 160;
        _notes.MaxHeight = 320;
        _notes.FontSize = 15;
        _notes.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        root.Children.Add(_notes);

        var (done, total) = s.TaskCounts();
        if (total > 0)
        {
            root.Children.Add(UI.Lbl("fs_checklist"));
            foreach (var line in (s.Checklist ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                bool d = line.StartsWith("1|");
                var t = new TextBlock { Text = (d ? "✔  " : "☐  ") + (line.Length > 2 ? line.Substring(2) : ""), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), Opacity = d ? 0.65 : 1 };
                root.Children.Add(t);
            }
        }

        if (s.HasInk)
        {
            root.Children.Add(UI.Lbl("fs_board"));
            try
            {
                var bytes = Db.GetSessionInk(s.Id);
                if (bytes != null && bytes.Length > 0)
                {
                    using var ms = new MemoryStream(bytes);
                    var ink = new InkCanvas { Background = Brushes.Transparent, Strokes = new StrokeCollection(ms), IsHitTestVisible = false };
                    var board = new Border { Background = new SolidColorBrush(Color.FromRgb(0x15, 0x1A, 0x24)), CornerRadius = new CornerRadius(14), Height = 380, Child = ink, ClipToBounds = true };
                    root.Children.Add(board);
                }
            }
            catch (Exception ex) { Log.Write("Board load: " + ex.Message); }
        }

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        footer.Children.Add(UI.Btn("save", (a, b) =>
        {
            _s.Notes = _notes.Text;
            Db.SaveSession(_s, false);
            DialogResult = true;
        }));
        footer.Children.Add(UI.Btn("cancel", (a, b) => DialogResult = false, true));
        root.Children.Add(footer);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }
}
