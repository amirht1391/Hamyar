using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Hamyar;

// Home page: live clock, quick timers, next alarm, what is due to study, next exam, next prayer - all in one place
public partial class MainWindow
{
    TextBlock _homeClock, _homeNextLeft;
    Item _homeNext;
    DispatcherTimer _homeTimer;
    string _homeMsg = "";

    void StartHomeTimer()
    {
        _homeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _homeTimer.Tick += (s, e) =>
        {
            if (PanelHome.Visibility != Visibility.Visible) return;
            var now = DateTime.Now;
            if (_homeClock != null) _homeClock.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (_homeNextLeft != null && _homeNext?.AlarmAt != null)
            {
                var left = _homeNext.AlarmAt.Value - now;
                if (left < TimeSpan.Zero) { Safe(BuildHome); return; }
                _homeNextLeft.Text = FormatLeftSec(left);
            }
        };
        _homeTimer.Start();
    }

    static string FormatLeftSec(TimeSpan d)
    {
        if (d.TotalHours >= 1) return FormatLeft(d);
        int m = (int)d.TotalMinutes, s = d.Seconds;
        return m + ":" + s.ToString("00");
    }

    static Button HomeBtn(string text, RoutedEventHandler click, bool ghost = true)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 6) };
        if (ghost) b.Style = UI.S("GhostButton");
        b.Click += click;
        return b;
    }

    void BuildHome()
    {
        if (HomeHost == null) return;
        var h = HomeHost;
        h.Children.Clear();
        var now = DateTime.Now;

        // ---- clock
        var clock = NewCard(h, "home_now", "");
        _homeClock = new TextBlock { Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), FontSize = 44, FontWeight = FontWeights.SemiBold, FlowDirection = System.Windows.FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
        _homeClock.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        clock.Children.Add(_homeClock);
        var date = new TextBlock { FontSize = 15, Margin = new Thickness(2, 0, 0, 8) };
        date.Text = Loc.Get("d_" + (int)now.DayOfWeek) + "   " + Jal.Date(now);
        clock.Children.Add(date);

        // ---- quick timers (one click = an alarm in N minutes)
        var quick = NewCard(h, "home_quick", "");
        var wrap = new WrapPanel();
        foreach (var m in new[] { 5, 10, 15, 20, 30, 45, 60 })
        {
            int mm = m;
            wrap.Children.Add(HomeBtn(m < 60 ? m + "m" : "1h", (s, e) => QuickTimer(mm)));
        }
        quick.Children.Add(wrap);
        if (_homeMsg.Length > 0)
        {
            var msg = new TextBlock { Text = _homeMsg, Margin = new Thickness(2, 2, 0, 0), FontWeight = FontWeights.SemiBold };
            msg.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            quick.Children.Add(msg);
        }

        // ---- next alarm
        var na = NewCard(h, "home_next_alarm", "");
        _homeNext = Db.GetItems(null).Where(i => i.Enabled && i.AlarmAt.HasValue && i.AlarmAt.Value > now).OrderBy(i => i.AlarmAt).FirstOrDefault();
        _homeNextLeft = null;
        if (_homeNext == null)
        {
            na.Children.Add(Muted("home_no_alarm", 14));
            na.Children.Add(HomeBtn(Loc.Get("home_new_alarm"), (s, e) => { NavTables.IsChecked = true; AddItem_Click(null, null); }, false));
        }
        else
        {
            na.Children.Add(new TextBlock { Text = _homeNext.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var when = new TextBlock { Text = Jal.Stamp(_homeNext.AlarmAt.Value), FontSize = 13, FlowDirection = System.Windows.FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
            when.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            na.Children.Add(when);
            _homeNextLeft = new TextBlock { FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 8), FlowDirection = System.Windows.FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
            _homeNextLeft.Text = FormatLeftSec(_homeNext.AlarmAt.Value - now);
            na.Children.Add(_homeNextLeft);
            var it = _homeNext;
            var row = new WrapPanel();
            row.Children.Add(HomeBtn(Loc.Get("home_turn_off"), (s, e) => { Db.SetEnabled(it.Id, false); RefreshItems(); BuildHome(); }));
            row.Children.Add(HomeBtn(Loc.Get("edit"), (s, e) => { if (new ItemDialog(this, it).ShowDialog() == true) { RefreshItems(); BuildHome(); } }));
            na.Children.Add(row);
        }

        // ---- study
        var st = NewCard(h, "home_review", "");
        int dt = Db.CountDueTopics(), dc = Db.CountDueCards();
        if (dt == 0 && dc == 0) st.Children.Add(Muted("home_nothing_due", 14));
        else
        {
            var row = new WrapPanel();
            if (dt > 0) row.Children.Add(HomeBtn(string.Format(Loc.Get("home_due_topics"), dt), (s, e) => { NavStudy.IsChecked = true; }, false));
            if (dc > 0) row.Children.Add(HomeBtn(string.Format(Loc.Get("home_due_cards"), dc), (s, e) => { NavCards.IsChecked = true; _cardCourseId = 0; _cardSubjectId = 0; StartReview(false); }, false));
            st.Children.Add(row);
            var reason = StudyWindow.Check(now);
            if (reason != StudyWindow.Reason.Open) st.Children.Add(Muted(reason == StudyWindow.Reason.Sleeping ? "block_sleep" : "block_school", 12.5));
        }

        // ---- exam
        var ex = NewCard(h, "home_exam", "");
        var nextExam = _exams.Where(x => !x.IsPast).OrderBy(x => x.When).FirstOrDefault();
        if (nextExam == null) ex.Children.Add(Muted("home_no_exam", 14));
        else
        {
            ex.Children.Add(new TextBlock { Text = nextExam.Title + "  ·  " + nextExam.DifficultyText, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var left = nextExam.When - now;
            var t = new TextBlock { Text = Jal.Stamp(nextExam.When) + "   ·   " + (left.TotalDays >= 1 ? string.Format(Loc.Get("exam_in_days"), (int)Math.Ceiling(left.TotalDays)) : FormatLeft(left)), Margin = new Thickness(0, 2, 0, 0), FlowDirection = System.Windows.FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
            t.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            ex.Children.Add(t);
        }

        // ---- prayer: the window we are in right now (adhan time, end of the window, duration), else the next prayer
        if (Db.PrayRange().count > 0)
        {
            var pr = NewCard(h, "home_prayer", "");
            var cur = PrayTimes.Current(now);
            if (cur != null)
            {
                var c = cur.Value;
                int endIndex = PrayTimes.Periods[c.period].to;
                pr.Children.Add(new TextBlock { Text = Loc.Get("pr_now_" + c.period), FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                var d = new TextBlock
                {
                    Text = string.Format(Loc.Get("pr_now_detail"), c.start.ToString("HH:mm", CultureInfo.InvariantCulture), Loc.Get(PrayTimes.Keys[endIndex]),
                        c.end.ToString("HH:mm", CultureInfo.InvariantCulture), FormatLeft(c.end - c.start), FormatLeft(c.end - now)),
                    Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap
                };
                d.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
                pr.Children.Add(d);
            }
            else
            {
                var nx = PrayTimes.Next(now);
                if (nx != null)
                {
                    var n = nx.Value;
                    pr.Children.Add(new TextBlock
                    {
                        Text = string.Format(Loc.Get("pr_next"), Loc.Get(PrayTimes.Keys[n.index]), n.when.ToString("HH:mm", CultureInfo.InvariantCulture), FormatLeft(n.when - now)),
                        FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
                    });
                }
            }
        }

        // ---- the guided tour, so new users can always find it
        BuildTourCard(h);
    }

    void QuickTimer(int minutes)
    {
        if (_boards.Count == 0) return;
        if (!_boards.Any(b => b.Id == _boardId)) _boardId = _boards[0].Id;
        var when = DateTime.Now.AddMinutes(minutes);
        when = new DateTime(when.Year, when.Month, when.Day, when.Hour, when.Minute, 0);
        var it = new Item { BoardId = _boardId, Title = "⏱ " + string.Format(Loc.Get("home_timer_title"), minutes), AlarmAt = when, Repeat = RepeatMode.None, Enabled = true };
        Db.SaveItem(it);
        _homeMsg = string.Format(Loc.Get("home_timer_set"), when.ToString("HH:mm", CultureInfo.InvariantCulture));
        RefreshItems();
        BuildHome();
    }

    static void FadeIn(FrameworkElement el)
    {
        if (el == null) return;
        var tt = new TranslateTransform(0, 14);
        el.RenderTransform = tt;
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    void ItemsList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        for (DependencyObject d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase) return;
            if (d is FrameworkElement fe && fe.DataContext is Item it)
            {
                if (new ItemDialog(this, it).ShowDialog() == true) RefreshItems();
                return;
            }
        }
    }
}
