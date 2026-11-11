using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

public class SessionOptions
{
    public Topic Topic;                 // null = free study
    public string Title = "";
    public int Mode;                    // 0 countdown, 1 pomodoro, 2 open (stopwatch)
    public int TotalMin = 45;           // countdown
    public int WorkMin = 25, BreakMin = 5, Cycles = 4;   // pomodoro
    public bool Lock = true;
    public string Plan = "";            // one task per line

    public int PlannedSeconds => Mode switch { 0 => TotalMin * 60, 1 => WorkMin * 60 * Cycles, _ => 0 };
}

/// <summary>Asks what to study, for how long, and whether to cover the screen. Remembers the last choices.</summary>
public class SessionSetupDialog : Window
{
    public SessionOptions Result { get; private set; }

    readonly List<Topic> _topics;
    readonly List<RadioButton> _topicChips = new();
    readonly List<RadioButton> _modeChips = new();
    readonly TextBox _goal = new(), _plan = new(), _total = new(), _work = new(), _break = new(), _cycles = new();
    readonly CheckBox _lock = new();
    readonly StackPanel _countPanel = new(), _pomoPanel = new();
    readonly TextBlock _endsText = new(), _openNote = new();

    public SessionSetupDialog(Window owner, List<Topic> topics, Topic preselect)
    {
        _topics = topics.Take(60).ToList();
        UI.Setup(this, owner, true);
        Title = Loc.Get("fs_setup_title");
        Width = 600;
        Height = 780;
        MinHeight = 560;

        var root = new StackPanel { Margin = new Thickness(20) };
        UI.BindFlow(root);

        // ---- topic
        root.Children.Add(UI.Lbl("fs_topic"));
        var tw = new WrapPanel();
        var free = new RadioButton { GroupName = "fstopic", Tag = -1, Style = UI.S("Chip"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2), IsChecked = preselect == null };
        UI.Bind(free, ContentControl.ContentProperty, "fs_topic_none");
        _topicChips.Add(free);
        tw.Children.Add(free);
        for (int i = 0; i < _topics.Count; i++)
        {
            var t = _topics[i];
            var rb = new RadioButton
            {
                GroupName = "fstopic", Tag = i, Style = UI.S("Chip"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2),
                Content = t.Title + (t.CourseName.Length > 0 ? "  ·  " + t.CourseName : ""), IsChecked = preselect != null && t.Id == preselect.Id
            };
            _topicChips.Add(rb);
            tw.Children.Add(rb);
        }
        root.Children.Add(new ScrollViewer { MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = tw });

        // ---- goal + plan
        root.Children.Add(UI.Lbl("fs_goal"));
        root.Children.Add(_goal);
        root.Children.Add(UI.Lbl("fs_plan"));
        _plan.AcceptsReturn = true;
        _plan.TextWrapping = TextWrapping.Wrap;
        _plan.Height = 74;
        _plan.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        root.Children.Add(_plan);

        // ---- timer mode
        root.Children.Add(UI.Lbl("fs_mode"));
        var mw = new WrapPanel();
        int savedMode = FocusUtil.Num(Db.GetSetting("fs_mode", "0"), 0, 0, 2);
        string[] modeKeys = { "fs_mode_count", "fs_mode_pomo", "fs_mode_open" };
        for (int i = 0; i < 3; i++)
        {
            var rb = UI.Chip("fsmode", i.ToString(), modeKeys[i], true);
            rb.IsChecked = i == savedMode;
            rb.Checked += (s, e) => ModeChanged();
            _modeChips.Add(rb);
            mw.Children.Add(rb);
        }
        root.Children.Add(mw);

        // countdown: minutes + presets
        _total.Text = Db.GetSetting("fs_total", "45");
        _total.Width = 90;
        _total.FlowDirection = FlowDirection.LeftToRight;
        _total.TextChanged += (s, e) => UpdateEnds();
        var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var m in new[] { 15, 25, 45, 60, 90, 120 })
        {
            int mm = m;
            var b = new Button { Style = UI.S("GhostButton"), Content = mm.ToString(), MinWidth = 52, Padding = new Thickness(10, 4, 10, 4) };
            b.Click += (s, e) => _total.Text = mm.ToString();
            presets.Children.Add(b);
        }
        var minRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var minLbl = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        UI.Bind(minLbl, TextBlock.TextProperty, "fs_minutes");
        minRow.Children.Add(minLbl);
        minRow.Children.Add(_total);
        _countPanel.Children.Add(minRow);
        _countPanel.Children.Add(presets);

        // pomodoro
        _work.Text = Db.GetSetting("fs_work", "25");
        _break.Text = Db.GetSetting("fs_break", "5");
        _cycles.Text = Db.GetSetting("fs_cycles", "4");
        foreach (var tb in new[] { _work, _break, _cycles })
        {
            tb.Width = 70;
            tb.FlowDirection = FlowDirection.LeftToRight;
            tb.Margin = new Thickness(0, 3, 14, 3);
            tb.TextChanged += (s, e) => UpdateEnds();
        }
        var pr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        void AddField(string key, TextBox tb)
        {
            var l = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            UI.Bind(l, TextBlock.TextProperty, key);
            pr.Children.Add(l);
            pr.Children.Add(tb);
        }
        AddField("fs_work", _work);
        AddField("fs_break", _break);
        AddField("fs_cycles", _cycles);
        _pomoPanel.Children.Add(pr);

        root.Children.Add(_countPanel);
        root.Children.Add(_pomoPanel);

        _endsText.Margin = new Thickness(0, 8, 0, 0);
        _endsText.FontWeight = FontWeights.SemiBold;
        _endsText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        root.Children.Add(_endsText);
        _openNote.TextWrapping = TextWrapping.Wrap;
        _openNote.FontSize = 12.5;
        _openNote.Margin = new Thickness(0, 8, 0, 0);
        _openNote.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(_openNote, TextBlock.TextProperty, "fs_open_note");
        root.Children.Add(_openNote);

        // ---- focus lock
        _lock.Margin = new Thickness(0, 16, 0, 0);
        _lock.IsChecked = Db.GetSetting("fs_lock", "1") == "1";
        UI.Bind(_lock, ContentControl.ContentProperty, "fs_lock");
        root.Children.Add(_lock);
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(note, TextBlock.TextProperty, "fs_lock_note");
        root.Children.Add(note);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        footer.Children.Add(UI.Btn("fs_begin", (s, e) => Begin()));
        footer.Children.Add(UI.Btn("cancel", (s, e) => DialogResult = false, true));
        root.Children.Add(footer);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        ModeChanged();
    }

    int Mode => _modeChips.FindIndex(r => r.IsChecked == true);

    void ModeChanged()
    {
        int m = Mode;
        _countPanel.Visibility = m == 0 ? Visibility.Visible : Visibility.Collapsed;
        _pomoPanel.Visibility = m == 1 ? Visibility.Visible : Visibility.Collapsed;
        _openNote.Visibility = m == 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEnds();
    }

    SessionOptions Read()
    {
        var o = new SessionOptions
        {
            Mode = Math.Max(0, Mode),
            TotalMin = FocusUtil.Num(_total.Text, 45, 1, 600),
            WorkMin = FocusUtil.Num(_work.Text, 25, 1, 240),
            BreakMin = FocusUtil.Num(_break.Text, 5, 1, 120),
            Cycles = FocusUtil.Num(_cycles.Text, 4, 1, 20),
            Lock = _lock.IsChecked == true,
            Title = _goal.Text.Trim(),
            Plan = _plan.Text
        };
        int ti = _topicChips.FindIndex(r => r.IsChecked == true);
        if (ti > 0) o.Topic = _topics[(int)_topicChips[ti].Tag];
        return o;
    }

    void UpdateEnds()
    {
        if (_endsText == null) return;
        var o = Read();
        if (o.Mode == 2) { _endsText.Text = ""; return; }
        int total = o.Mode == 0 ? o.TotalMin * 60 : o.WorkMin * 60 * o.Cycles + o.BreakMin * 60 * (o.Cycles - 1);
        var end = DateTime.Now.AddSeconds(total);
        _endsText.Text = string.Format(Loc.Get("fs_ends"), end.ToString("HH:mm", CultureInfo.InvariantCulture)) + "   ·   " + FocusUtil.Dur(total);
    }

    void Begin()
    {
        var o = Read();
        Db.SetSetting("fs_mode", o.Mode.ToString());
        Db.SetSetting("fs_total", o.TotalMin.ToString());
        Db.SetSetting("fs_work", o.WorkMin.ToString());
        Db.SetSetting("fs_break", o.BreakMin.ToString());
        Db.SetSetting("fs_cycles", o.Cycles.ToString());
        Db.SetSetting("fs_lock", o.Lock ? "1" : "0");
        Result = o;
        DialogResult = true;
    }
}
