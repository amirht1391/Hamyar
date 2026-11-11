using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hamyar;

/// <summary>
/// The study session screen. With the focus lock on it covers every monitor and stays on top; the timer (countdown /
/// pomodoro / stopwatch), a checklist, notes, a drawing board, a flashcard maker and a calculator are all inside it,
/// so nothing has to be written on paper. Notes, checklist and board are saved automatically while you work.
/// </summary>
public class FocusWindow : Window
{
    enum Phase { Work, Break }

    class CheckItem { public string Text = ""; public bool Done; }

    readonly SessionOptions _o;
    readonly StudySession _s = new();
    readonly Topic _topic;

    // ---- timer state (wall-clock based, so it stays correct if the UI is busy)
    Phase _phase = Phase.Work;
    int _round = 1;
    double _phaseTotal;                 // seconds of the current phase, 0 = open-ended
    TimeSpan _accum;                    // time of this phase already counted while it was not running
    DateTime? _runStart;                // when the phase was last (re)started; null while paused
    DateTime _lastTick = DateTime.Now;
    double _focusedBase;                // seconds of finished focus phases
    bool _paused, _ended, _enforce, _allowClose, _inkDirty;

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromSeconds(15) };

    // ---- UI
    Grid _content;
    TextBlock _timeText, _phaseText, _roundText, _focusText, _lockText, _sleepNote;
    Grid _barTrack;
    Border _barFill;
    Button _pauseBtn, _plusBtn, _skipBtn, _minBtn;
    StackPanel _checkHost;
    TextBox _checkAdd, _notes, _calcIn;
    InkCanvas _ink;
    readonly List<CheckItem> _items = new();
    readonly List<FrameworkElement> _panels = new();
    StackPanel _calcHist;
    TextBlock _calcOut;
    Grid _overlay;

    static readonly Color BoardBg = Color.FromRgb(0x15, 0x1A, 0x24);

    public FocusWindow(Window owner, SessionOptions o)
    {
        _o = o;
        _topic = o.Topic;
        Owner = owner;
        Title = Loc.Get("fs_setup_title");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        bool dark = Cfg.Theme == 0;
        Background = new SolidColorBrush(dark ? Color.FromRgb(0x0B, 0x0E, 0x14) : Color.FromRgb(0xEE, 0xF1, 0xF7));
        this.SetResourceReference(FontFamilyProperty, "AppFont");
        FontSize = 14;
        this.SetResourceReference(ForegroundProperty, "FgBrush");

        // the session row exists from the first second, so a crash never loses the notes
        _s.TopicId = _topic?.Id;
        _s.Title = !string.IsNullOrWhiteSpace(o.Title) ? o.Title : (_topic?.Title ?? Loc.Get("fs_free"));
        _s.StartedAt = DateTime.Now;
        _s.PlannedSeconds = o.PlannedSeconds;
        _s.Mode = o.Mode;
        _s.Locked = o.Lock;
        foreach (var line in (o.Plan ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (line.Trim().Length > 0) _items.Add(new CheckItem { Text = line.Trim() });
        Db.AddSession(_s);

        BuildUi();
        PlaceWindow();
        RebuildChecklist();

        _phaseTotal = o.Mode == 0 ? o.TotalMin * 60 : o.Mode == 1 ? o.WorkMin * 60 : 0;
        _runStart = DateTime.Now;
        _lastTick = DateTime.Now;
        _enforce = o.Lock;

        _tick.Tick += (s, e) => { try { Tick(); } catch (Exception ex) { Log.Write("Focus tick: " + ex.Message); } };
        _save.Tick += (s, e) => SaveNow();
        Loaded += (s, e) =>
        {
            _tick.Start();
            _save.Start();
            if (_enforce) { Topmost = true; Activate(); }
            UpdateChrome();
            Tick();
            _notes.Focus();
        };
        Closing += (s, e) => { if (!_allowClose) e.Cancel = true; };     // Alt+F4 cannot end the session by accident
        Closed += (s, e) =>
        {
            _tick.Stop();
            _save.Stop();
            Application.Current.SessionEnding -= OnSessionEnding;
            AudioService.Stop();
        };
        Application.Current.SessionEnding += OnSessionEnding;           // Windows shutting down must never be blocked by the lock
    }

    void OnSessionEnding(object sender, SessionEndingCancelEventArgs e) { _allowClose = true; SaveNow(); }

    // ------------------------------------------------------------------ window placement
    void PlaceWindow()
    {
        if (_o.Lock)
        {
            // one window over every monitor; the content sits on the primary one
            WindowState = WindowState.Normal;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            _content.HorizontalAlignment = HorizontalAlignment.Stretch;
            _content.VerticalAlignment = VerticalAlignment.Stretch;
            _content.Margin = new Thickness(
                -SystemParameters.VirtualScreenLeft,
                -SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth + SystemParameters.VirtualScreenLeft - SystemParameters.PrimaryScreenWidth,
                SystemParameters.VirtualScreenHeight + SystemParameters.VirtualScreenTop - SystemParameters.PrimaryScreenHeight);
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    // ------------------------------------------------------------------ building the screen
    TextBlock Txt(string text, double size = 14, bool bold = false, bool sub = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, sub ? "SubFgBrush" : "FgBrush");
        return t;
    }

    Border Pane(UIElement child, Thickness? margin = null)
    {
        var b = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(18), Margin = margin ?? new Thickness(0), Child = child };
        b.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        b.BorderThickness = new Thickness(1);
        return b;
    }

    void BuildUi()
    {
        _content = new Grid { Margin = new Thickness(22) };
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        UI.BindFlow(_content);

        // ---- top bar
        var top = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(Txt(_s.Title, 22, true));
        if (_topic != null) left.Children.Add(Txt(_topic.CourseName + "  ·  " + _topic.Title, 13, false, true));
        _lockText = Txt("🔒 " + Loc.Get("fs_locked"), 12.5, false, true);
        left.Children.Add(_lockText);
        top.Children.Add(left);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _minBtn = UI.Btn("fs_minimize", (s, e) => WindowState = WindowState.Minimized, true);
        _skipBtn = UI.Btn("fs_skip_break", (s, e) => PhaseDone(), true);
        _plusBtn = UI.Btn("fs_plus5", (s, e) => { if (_phaseTotal > 0) _phaseTotal += 300; }, true);
        _pauseBtn = UI.Btn("fs_pause", (s, e) => TogglePause(), true);
        var finish = UI.Btn("fs_finish", (s, e) => FinishClicked());
        foreach (var b in new Button[] { _minBtn, _skipBtn, _plusBtn, _pauseBtn, finish }) btns.Children.Add(b);
        Grid.SetColumn(btns, 1);
        top.Children.Add(btns);
        Grid.SetRow(top, 0);
        _content.Children.Add(top);

        // ---- main area: timer + checklist | tabs
        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(main, 1);
        _content.Children.Add(main);

        var leftCol = new Grid { Margin = new Thickness(0, 0, 16, 0) };
        leftCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var timer = new StackPanel();
        _phaseText = Txt("", 15, true, true);
        _phaseText.HorizontalAlignment = HorizontalAlignment.Center;
        _roundText = Txt("", 13, false, true);
        _roundText.HorizontalAlignment = HorizontalAlignment.Center;
        _timeText = new TextBlock { FontSize = 76, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, Text = "00:00" };
        _barTrack = new Grid { Height = 10, Margin = new Thickness(0, 6, 0, 10) };
        var track = new Border { CornerRadius = new CornerRadius(5) };
        track.SetResourceReference(Border.BackgroundProperty, "LineBrush");
        _barFill = new Border { CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _barFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _barTrack.Children.Add(track);
        _barTrack.Children.Add(_barFill);
        _focusText = Txt("", 13, false, true);
        _focusText.HorizontalAlignment = HorizontalAlignment.Center;
        _sleepNote = Txt("", 12, false, true);
        _sleepNote.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var el in new UIElement[] { _phaseText, _roundText, _timeText, _barTrack, _focusText, _sleepNote }) timer.Children.Add(el);
        var timerCard = Pane(timer, new Thickness(0, 0, 0, 14));
        Grid.SetRow(timerCard, 0);
        leftCol.Children.Add(timerCard);

        // checklist
        var cl = new Grid();
        cl.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cl.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cl.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var clTitle = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        UI.Bind(clTitle, TextBlock.TextProperty, "fs_checklist");
        cl.Children.Add(clTitle);
        var addRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _checkAdd = new TextBox();
        UI.Bind(_checkAdd, FrameworkElement.ToolTipProperty, "fs_task_hint");
        _checkAdd.KeyDown += (s, e) => { if (e.Key == Key.Enter) { AddTask(); e.Handled = true; } };
        addRow.Children.Add(_checkAdd);
        var addBtn = UI.IconBtn("\uE710", (s, e) => AddTask(), "fs_add_task");
        Grid.SetColumn(addBtn, 1);
        addRow.Children.Add(addBtn);
        Grid.SetRow(addRow, 1);
        cl.Children.Add(addRow);
        _checkHost = new StackPanel();
        var clScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _checkHost };
        Grid.SetRow(clScroll, 2);
        cl.Children.Add(clScroll);
        var clCard = Pane(cl);
        Grid.SetRow(clCard, 1);
        leftCol.Children.Add(clCard);
        Grid.SetColumn(leftCol, 0);
        main.Children.Add(leftCol);

        // ---- right: tabs
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        string[] tabKeys = { "fs_tab_notes", "fs_tab_board", "fs_tab_calc" };
        for (int i = 0; i < tabKeys.Length; i++)
        {
            var rb = UI.Chip("fstab", i.ToString(), tabKeys[i], true);
            rb.Padding = new Thickness(14, 6, 14, 6);
            rb.IsChecked = i == 0;
            rb.Checked += (s, e) => ShowTab(int.Parse((string)((RadioButton)s).Tag));
            tabs.Children.Add(rb);
        }
        right.Children.Add(tabs);

        var host = new Grid();
        Grid.SetRow(host, 1);
        _panels.Add(BuildNotesPanel());
        _panels.Add(BuildBoardPanel());
        _panels.Add(BuildCalcPanel());
        foreach (var p in _panels) host.Children.Add(p);
        right.Children.Add(host);
        Grid.SetColumn(right, 1);
        main.Children.Add(right);
        ShowTab(0);

        _overlay = new Grid { Visibility = Visibility.Collapsed };
        var root = new Grid();
        root.Children.Add(_content);
        root.Children.Add(_overlay);
        Content = root;
    }

    void ShowTab(int i)
    {
        for (int k = 0; k < _panels.Count; k++) _panels[k].Visibility = k == i ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- notes
    FrameworkElement BuildNotesPanel()
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var sym in new[] { "• ", "★ ", "? ", "→ ", "= ", "✔ " })
        {
            var s2 = sym;
            var b = new Button { Style = UI.S("GhostButton"), Content = s2.Trim(), MinWidth = 40, Padding = new Thickness(10, 4, 10, 4) };
            b.Click += (s, e) => InsertAtCaret(s2);
            bar.Children.Add(b);
        }
        var stamp = new Button { Style = UI.S("GhostButton"), Content = "⏱", MinWidth = 40, Padding = new Thickness(10, 4, 10, 4) };
        stamp.Click += (s, e) => InsertAtCaret("[" + FocusUtil.Clock(FocusedSeconds) + "] ");
        bar.Children.Add(stamp);
        Grid.SetRow(bar, 0);
        g.Children.Add(bar);

        if (_topic != null && !string.IsNullOrWhiteSpace(_topic.Notes))
        {
            var prev = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var l = Txt(Loc.Get("fs_topic_notes"), 12, true, true);
            prev.Children.Add(l);
            prev.Children.Add(new ScrollViewer { MaxHeight = 70, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Txt(_topic.Notes, 13, false, true) });
            Grid.SetRow(prev, 1);
            g.Children.Add(prev);
        }

        _notes = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, FontSize = 17,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(12)
        };
        Grid.SetRow(_notes, 2);
        g.Children.Add(_notes);

        var hint = Txt(Loc.Get("fs_notes_hint"), 12, false, true);
        hint.Margin = new Thickness(2, 6, 0, 0);
        Grid.SetRow(hint, 3);
        g.Children.Add(hint);
        return g;
    }

    void InsertAtCaret(string text)
    {
        int i = _notes.CaretIndex;
        _notes.Text = _notes.Text.Insert(i, text);
        _notes.CaretIndex = i + text.Length;
        _notes.Focus();
    }

    // ---- drawing board
    FrameworkElement BuildBoardPanel()
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _ink = new InkCanvas
        {
            Background = Brushes.Transparent,
            DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.White, Width = 3, Height = 3, FitToCurve = true }
        };
        _ink.Strokes.StrokesChanged += (s, e) => _inkDirty = true;

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var hex in new[] { "#FFFFFF", "#FACC15", "#F87171", "#38BDF8", "#4ADE80" })
        {
            var col = (Color)ColorConverter.ConvertFromString(hex);
            var sw = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(4, 2, 4, 2), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(col), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent
            };
            sw.MouseLeftButtonDown += (s, e) =>
            {
                _ink.EditingMode = InkCanvasEditingMode.Ink;
                _ink.DefaultDrawingAttributes.Color = col;
            };
            bar.Children.Add(sw);
        }
        foreach (var (label, w) in new[] { ("S", 2.0), ("M", 4.0), ("L", 8.0) })
        {
            double ww = w;
            var b = new Button { Style = UI.S("GhostButton"), Content = label, MinWidth = 38, Padding = new Thickness(8, 4, 8, 4) };
            b.Click += (s, e) =>
            {
                _ink.EditingMode = InkCanvasEditingMode.Ink;
                _ink.DefaultDrawingAttributes.Width = ww;
                _ink.DefaultDrawingAttributes.Height = ww;
            };
            bar.Children.Add(b);
        }
        var pen = UI.Btn("fs_pen", (s, e) => _ink.EditingMode = InkCanvasEditingMode.Ink, true);
        var eraser = UI.Btn("fs_eraser", (s, e) => _ink.EditingMode = InkCanvasEditingMode.EraseByStroke, true);
        var undo = UI.Btn("fs_undo", (s, e) => { if (_ink.Strokes.Count > 0) _ink.Strokes.RemoveAt(_ink.Strokes.Count - 1); }, true);
        var clear = UI.Btn("fs_clear", (s, e) =>
        {
            if (_ink.Strokes.Count > 0 && Dialogs.Confirm(this, Loc.Get("fs_clear_confirm"))) _ink.Strokes.Clear();
        }, true);
        foreach (var b in new Button[] { pen, eraser, undo, clear }) bar.Children.Add(b);
        Grid.SetRow(bar, 0);
        g.Children.Add(bar);

        var board = new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(BoardBg), Child = _ink, ClipToBounds = true };
        Grid.SetRow(board, 1);
        g.Children.Add(board);
        return g;
    }

    // ---- calculator
    FrameworkElement BuildCalcPanel()
    {
        var sp = new StackPanel();
        var hint = Txt(Loc.Get("fs_calc_hint"), 12.5, false, true);
        hint.Margin = new Thickness(0, 0, 0, 8);
        sp.Children.Add(hint);

        var deg = UI.Chip("fscalc", "d", "fs_deg", true);
        var rad = UI.Chip("fscalc", "r", "fs_rad", true);
        deg.IsChecked = true;
        deg.Checked += (s, e) => EvalCalc();
        rad.Checked += (s, e) => EvalCalc();
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        chips.Children.Add(deg);
        chips.Children.Add(rad);
        sp.Children.Add(chips);

        _calcIn = new TextBox { FontSize = 24, FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(12, 8, 12, 8) };
        _calcIn.TextChanged += (s, e) => EvalCalc();
        _calcIn.KeyDown += (s, e) => { if (e.Key == Key.Enter) { PushCalc(deg.IsChecked == true); e.Handled = true; } };
        sp.Children.Add(_calcIn);
        _calcOut = new TextBlock { FontSize = 38, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 10, 0, 10), FlowDirection = FlowDirection.LeftToRight };
        _calcOut.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        sp.Children.Add(_calcOut);
        _calcHist = new StackPanel();
        sp.Children.Add(_calcHist);
        calcDeg = deg;
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
    }

    RadioButton calcDeg;

    void EvalCalc()
    {
        if (_calcIn == null || _calcOut == null) return;
        if (MiniCalc.TryEval(_calcIn.Text, calcDeg?.IsChecked == true, out var v)) _calcOut.Text = "= " + MiniCalc.Format(v);
        else _calcOut.Text = _calcIn.Text.Trim().Length == 0 ? "" : "…";
    }

    void PushCalc(bool degrees)
    {
        if (!MiniCalc.TryEval(_calcIn.Text, degrees, out var v)) return;
        var expr = _calcIn.Text.Trim();
        var result = MiniCalc.Format(v);
        var row = Txt(expr + "  =  " + result, 15, false, true);
        row.FlowDirection = FlowDirection.LeftToRight;
        row.Cursor = Cursors.Hand;
        row.Margin = new Thickness(0, 3, 0, 3);
        row.MouseLeftButtonUp += (s, e) => { _calcIn.Text = result; _calcIn.CaretIndex = _calcIn.Text.Length; _calcIn.Focus(); };
        _calcHist.Children.Insert(0, row);
        while (_calcHist.Children.Count > 15) _calcHist.Children.RemoveAt(_calcHist.Children.Count - 1);
        _calcIn.Text = result;
        _calcIn.CaretIndex = result.Length;
    }

    // ---- checklist
    void AddTask()
    {
        var t = _checkAdd.Text.Trim();
        if (t.Length == 0) return;
        _items.Add(new CheckItem { Text = t });
        _checkAdd.Text = "";
        RebuildChecklist();
        _checkAdd.Focus();
    }

    void RebuildChecklist()
    {
        _checkHost.Children.Clear();
        foreach (var it in _items.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cb = new CheckBox { IsChecked = it.Done, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = it.Text, TextWrapping = TextWrapping.Wrap, TextDecorations = it.Done ? TextDecorations.Strikethrough : null, Opacity = it.Done ? 0.55 : 1 };
            cb.Content = label;
            cb.Click += (s, e) =>
            {
                it.Done = cb.IsChecked == true;
                label.TextDecorations = it.Done ? TextDecorations.Strikethrough : null;
                label.Opacity = it.Done ? 0.55 : 1;
            };
            row.Children.Add(cb);
            var del = UI.IconBtn("\uE711", (s, e) => { _items.Remove(it); RebuildChecklist(); });
            del.Padding = new Thickness(6, 2, 6, 2);
            del.MinWidth = 28;
            del.FontSize = 11;
            Grid.SetColumn(del, 1);
            row.Children.Add(del);
            _checkHost.Children.Add(row);
        }
    }

    string SerializeChecklist() => string.Join("\n", _items.Select(i => (i.Done ? "1|" : "0|") + i.Text.Replace("\n", " ")));

    // ------------------------------------------------------------------ timer
    TimeSpan Elapsed => _accum + (_runStart.HasValue ? DateTime.Now - _runStart.Value : TimeSpan.Zero);

    double FocusedSeconds => _focusedBase + (_phase == Phase.Work ? Math.Min(Elapsed.TotalSeconds, _phaseTotal > 0 ? _phaseTotal : double.MaxValue) : 0);

    void Tick()
    {
        if (_ended) return;
        var now = DateTime.Now;
        if (_runStart.HasValue && (now - _lastTick).TotalSeconds > 120)
        {
            // the computer slept or hibernated: that time was not study time
            _accum += _lastTick - _runStart.Value;
            _runStart = null;
            _paused = true;
            _sleepNote.Text = Loc.Get("fs_sleep_pause");
            UpdateChrome();
        }
        _lastTick = now;
        if (_enforce) KeepOnTop();

        double el = Elapsed.TotalSeconds;
        if (_phaseTotal > 0)
        {
            double left = _phaseTotal - el;
            if (left <= 0 && !_paused) { PhaseDone(); return; }
            _timeText.Text = FocusUtil.Clock(Math.Ceiling(Math.Max(0, left)));
            double frac = Math.Clamp(el / _phaseTotal, 0, 1);
            _barFill.Width = _barTrack.ActualWidth * frac;
            _barTrack.Visibility = Visibility.Visible;
        }
        else
        {
            _timeText.Text = FocusUtil.Clock(el);
            _barTrack.Visibility = Visibility.Collapsed;
        }
        _focusText.Text = string.Format(Loc.Get("fs_focused"), FocusUtil.Dur((int)FocusedSeconds));
    }

    void KeepOnTop()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        // an alarm popup or a confirm dialog of this app must stay usable: only take the screen back when nothing else of ours is in front
        bool otherOfOurs = Application.Current.Windows.OfType<Window>().Any(w => w != this && w.IsVisible && (w.IsActive || w.Topmost));
        if (!IsActive && !otherOfOurs)
        {
            Topmost = false;
            Topmost = true;
            Activate();
        }
    }

    void StartPhase(Phase p, double seconds)
    {
        _phase = p;
        _phaseTotal = seconds;
        _accum = TimeSpan.Zero;
        _runStart = DateTime.Now;
        _paused = false;
        _sleepNote.Text = "";
        UpdateChrome();
    }

    /// <summary>The current phase ran out (or the break was skipped): next phase, or the end of the session.</summary>
    void PhaseDone()
    {
        if (_ended) return;
        if (_phase == Phase.Work)
        {
            _focusedBase += _phaseTotal > 0 ? _phaseTotal : Elapsed.TotalSeconds;
            if (_o.Mode == 1 && _round < _o.Cycles)
            {
                System.Media.SystemSounds.Asterisk.Play();
                SetEnforce(false);
                StartPhase(Phase.Break, _o.BreakMin * 60);
            }
            else EndSession(true);
        }
        else
        {
            _round++;
            System.Media.SystemSounds.Asterisk.Play();
            StartPhase(Phase.Work, _o.WorkMin * 60);
            SetEnforce(_o.Lock);
        }
    }

    void SetEnforce(bool on)
    {
        _enforce = on && _o.Lock;
        Topmost = _enforce;
        if (_enforce) { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
        UpdateChrome();
    }

    void TogglePause()
    {
        if (_ended) return;
        if (_paused)
        {
            _runStart = DateTime.Now;
            _lastTick = DateTime.Now;
            _paused = false;
            _sleepNote.Text = "";
        }
        else if (_runStart.HasValue)
        {
            _accum += DateTime.Now - _runStart.Value;
            _runStart = null;
            _paused = true;
        }
        UpdateChrome();
    }

    void UpdateChrome()
    {
        if (_phaseText == null) return;
        _phaseText.Text = _paused ? Loc.Get("fs_paused") : Loc.Get(_phase == Phase.Work ? "fs_focus" : "fs_break_phase");
        _roundText.Text = _o.Mode == 1 ? string.Format(Loc.Get("fs_round"), _round, _o.Cycles) : "";
        _roundText.Visibility = _o.Mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        UI.Bind(_pauseBtn, ContentControl.ContentProperty, _paused ? "fs_resume" : "fs_pause");
        _skipBtn.Visibility = _phase == Phase.Break ? Visibility.Visible : Visibility.Collapsed;
        _plusBtn.Visibility = _phase == Phase.Work && _phaseTotal > 0 ? Visibility.Visible : Visibility.Collapsed;
        _minBtn.Visibility = !_enforce && !_ended ? Visibility.Visible : Visibility.Collapsed;
        _lockText.Visibility = _enforce ? Visibility.Visible : Visibility.Collapsed;
        _pauseBtn.Visibility = _ended ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------ saving
    void SaveNow()
    {
        try
        {
            _s.Notes = _notes.Text;
            _s.Checklist = SerializeChecklist();
            _s.Seconds = (int)FocusedSeconds;
            bool ink = _inkDirty;
            if (ink)
            {
                if (_ink.Strokes.Count == 0) _s.Ink = null;
                else
                {
                    using var ms = new MemoryStream();
                    _ink.Strokes.Save(ms);
                    _s.Ink = ms.ToArray();
                }
                _s.HasInk = _s.Ink != null;
            }
            Db.SaveSession(_s, ink);
            _inkDirty = false;
        }
        catch (Exception ex) { Log.Write("Session save: " + ex.Message); }
    }

    // ------------------------------------------------------------------ ending
    void FinishClicked()
    {
        if (_ended) return;
        if (!Dialogs.Confirm(this, Loc.Get("fs_confirm_end"), false)) return;
        EndSession(false);
    }

    void EndSession(bool auto)
    {
        if (_ended) return;
        // the focused time includes the phase that was running when the button was pressed
        double focused = FocusedSeconds;
        if (auto && _phase == Phase.Work) focused = _focusedBase;
        _ended = true;
        _tick.Stop();
        _save.Stop();
        SetEnforce(false);
        Topmost = false;
        SaveNow();
        _s.Seconds = (int)focused;
        Db.SaveSession(_s, false);
        if (auto) AudioService.Play(AudioService.DefaultSound, false);
        UpdateChrome();
        ShowSummary();
    }

    void ShowSummary()
    {
        var (done, total) = _s.TaskCounts();
        var sp = new StackPanel { Width = 460 };
        var title = Txt(Loc.Get("fs_summary_title"), 26, true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(title);
        var big = new TextBlock { Text = FocusUtil.Dur(_s.Seconds), FontSize = 48, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
        sp.Children.Add(big);
        if (total > 0) sp.Children.Add(Txt(string.Format(Loc.Get("fs_sum_tasks"), done, total), 14, false, true));

        var rl = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 4) };
        UI.Bind(rl, TextBlock.TextProperty, "fs_rate");
        sp.Children.Add(rl);
        var stars = new WrapPanel();
        var starChips = new List<RadioButton>();
        for (int i = 1; i <= 5; i++)
        {
            var rb = new RadioButton { GroupName = "fsrate", Tag = i, Content = new string('★', i), Style = UI.S("Chip"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2), IsChecked = i == 3 };
            starChips.Add(rb);
            stars.Children.Add(rb);
        }
        sp.Children.Add(stars);

        CheckBox addNotes = null, markReviewed = null;
        if (_topic != null)
        {
            if (!string.IsNullOrWhiteSpace(_notes.Text))
            {
                addNotes = new CheckBox { Margin = new Thickness(0, 14, 0, 0) };
                UI.Bind(addNotes, ContentControl.ContentProperty, "fs_add_notes_topic");
                sp.Children.Add(addNotes);
            }
            markReviewed = new CheckBox { Margin = new Thickness(0, 8, 0, 0), IsChecked = _topic.IsDue };
            UI.Bind(markReviewed, ContentControl.ContentProperty, "fs_mark_reviewed");
            sp.Children.Add(markReviewed);
        }

        var ok = UI.Btn("fs_done", (s, e) =>
        {
            try
            {
                _s.Rating = (int)starChips.First(r => r.IsChecked == true).Tag;
                Db.SaveSession(_s, false);
                if (_topic != null)
                {
                    if (addNotes?.IsChecked == true)
                        _topic.Notes = (string.IsNullOrWhiteSpace(_topic.Notes) ? "" : _topic.Notes.TrimEnd() + "\n\n") + "— " + Jal.Date(DateTime.Today) + " —\n" + _notes.Text.Trim();
                    if (markReviewed?.IsChecked == true) ReviewEngine.Answer(_topic, true, DateTime.Now, _topic.Exam);
                    if (addNotes?.IsChecked == true || markReviewed?.IsChecked == true) Db.SaveTopic(_topic);
                }
                // an empty, very short session leaves no clutter behind
                if (_s.Seconds < 30 && string.IsNullOrWhiteSpace(_s.Notes) && total == 0 && !_s.HasInk) Db.DeleteSession(_s.Id);
            }
            catch (Exception ex) { Log.Write("Session finish: " + ex.Message); }
            _allowClose = true;
            DialogResult = true;
        });
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.MinWidth = 140;
        ok.Margin = new Thickness(0, 22, 0, 0);
        sp.Children.Add(ok);

        var card = Pane(sp);
        card.Padding = new Thickness(30);
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.SetResourceReference(Border.BackgroundProperty, "MenuBrush");
        UI.BindFlow(_overlay);
        _overlay.Children.Clear();
        _overlay.Children.Add(card);
        _overlay.Margin = _content.Margin;
        _overlay.Visibility = Visibility.Visible;
        _content.IsEnabled = false;
        _content.Opacity = 0.25;
    }
}
