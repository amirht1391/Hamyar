using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hamyar;

// The window is split in several files: this one = start-up, navigation, tables/items, alarms.
// MainWindow.Study.cs, MainWindow.Cards.cs, MainWindow.Timetable.cs, MainWindow.Settings.cs hold the rest.
public partial class MainWindow : Window
{
    long _boardId;
    List<Board> _boards = new();

    Scheduler _sched;
    DispatcherTimer _dayTimer;
    DateTime _today = DateTime.Today;
    bool _exit;
    StudyWindow.Reason _lastWindowReason = StudyWindow.Reason.Open;

    public MainWindow()
    {
        InitializeComponent();

        SourceInitialized += (s, e) => Glass.Apply(this, Cfg.Theme == 0, Cfg.Backdrop);
        Loaded += OnLoaded;
        PreviewKeyDown += Window_PreviewKeyDown;

        Loc.I.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Lang") OnLangChanged();
        };
    }

    static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Write("Init step failed: " + ex); }
    }

    /// <summary>Restores the saved order of the side-bar tabs and lets the user drag them.</summary>
    void SetupNavOrder()
    {
        var tabs = NavPanel.Children.OfType<RadioButton>().ToList();
        var saved = (Cfg.NavOrder ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        int at = 0;
        foreach (var tag in saved)
        {
            var t = tabs.FirstOrDefault(r => (r.Tag as string) == tag);
            if (t == null) continue;
            NavPanel.Children.Remove(t);
            NavPanel.Children.Insert(at++, t);
        }
        foreach (var t in tabs) ChipBar.AttachTabs(NavPanel, t, order => Cfg.NavOrder = string.Join(",", order));
    }

    static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    // ------------------------------------------------------------------ startup
    void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadBoards();
        LoadExams();     // loaded before courses/topics so their exam-picker dialogs always have the list ready
        LoadCourses();
        SetupNavOrder();
        ApplyTabs();
        ShowPanel("home");
        StartHomeTimer();

        _sched = new Scheduler();
        _sched.Fired += OnFired;
        _sched.Start();
        StartPlanAlarms();

        _lastWindowReason = StudyWindow.Check(DateTime.Now);

        // once a minute: a new day changes what is due, and the timetable may switch to the next school day
        _dayTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _dayTimer.Tick += (s, a) =>
        {
            // only the page that is on screen is rebuilt (the others refresh when opened); the tab badges always update.
            // Quick-edit and a running review are never disturbed.
            bool newDay = _today != DateTime.Today;
            if (newDay) { _today = DateTime.Today; Safe(BuildTimetable); }
            Safe(UpdateBadges);
            if (PanelStudy.Visibility == Visibility.Visible) Safe(RefreshTopics);
            if (PanelCards.Visibility == Visibility.Visible && !_cardEdit && ReviewPane.Visibility != Visibility.Visible) Safe(RefreshCards);
            if (!newDay) Safe(RefreshNextDay);
            if (PanelPrayer.Visibility == Visibility.Visible) Safe(BuildPrayer);
            if (PanelHome.Visibility == Visibility.Visible) Safe(BuildHome);
            Safe(CheckWakeNudge);
        };
        _dayTimer.Start();

        // the big pages are built after the window is already on screen
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Safe(BuildSettings);
            Safe(BuildHelp);
            Safe(BuildTimetable);
        }));
    }

    /// <summary>Fires the "time to review" popup the moment the study window opens (wake-up, or the end of
    /// school on a school day) - but only on that transition, once, and only if something is actually due.</summary>
    void CheckWakeNudge()
    {
        var now = DateTime.Now;
        var reason = StudyWindow.Check(now);
        bool justOpened = reason == StudyWindow.Reason.Open && _lastWindowReason != StudyWindow.Reason.Open;
        _lastWindowReason = reason;
        if (!justOpened) return;

        int dueTopics = Db.CountDueTopics();
        int dueCards = Db.CountDueCards();
        if (dueTopics == 0 && dueCards == 0) return;

        new WakeNudgePopup(this, dueTopics, dueCards,
            goStudy: () => { NavStudy.IsChecked = true; },
            goCards: () => { NavCards.IsChecked = true; }).Show();
    }

    void OnLangChanged()
    {
        if (!IsLoaded) return;
        RefreshItems();
        RefreshTopics();
        RefreshCards();
        Safe(RefreshExams);
        Safe(BuildHelp);
        Safe(BuildTimetable);
        Safe(BuildPrayer);
        Safe(BuildHome);
        Safe(BuildPlanner);
        Safe(RefreshBackupList);
        // first run: walk the new user through every section
        if (Db.GetSetting("tour_done", "0") != "1")
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += (s2, e2) => { t.Stop(); Safe(StartTour); };
            t.Start();
        }
    }

    // ------------------------------------------------------------------ navigation
    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ShowPanel((string)((RadioButton)sender).Tag);
        Anim.NavPop((RadioButton)sender);
    }

    void ShowPanel(string name)
    {
        PanelHome.Visibility = Vis(name == "home");
        PanelPlanner.Visibility = Vis(name == "planner");
        PanelTables.Visibility = Vis(name == "tables");
        PanelStudy.Visibility = Vis(name == "study");
        PanelCards.Visibility = Vis(name == "cards");
        PanelTimetable.Visibility = Vis(name == "timetable");
        PanelPrayer.Visibility = Vis(name == "prayer");
        PanelExam.Visibility = Vis(name == "exam");
        PanelSettings.Visibility = Vis(name == "settings");
        PanelHelp.Visibility = Vis(name == "help");

        string key = name switch
        {
            "home" => "nav_home",
            "planner" => "nav_planner",
            "study" => "nav_study",
            "cards" => "nav_cards",
            "timetable" => "nav_timetable",
            "prayer" => "nav_prayer",
            "exam" => "nav_exam",
            "settings" => "nav_settings",
            "help" => "nav_help",
            _ => "nav_tables"
        };
        UI.Bind(PageTitle, TextBlock.TextProperty, key);
        Anim.Title(PageTitle);

        FrameworkElement shown = name switch { "home" => PanelHome, "planner" => PanelPlanner, "tables" => PanelTables, "study" => PanelStudy, "cards" => PanelCards, "timetable" => PanelTimetable, "prayer" => PanelPrayer, "exam" => PanelExam, "settings" => PanelSettings, "help" => PanelHelp, _ => null };
        Anim.PanelIn(shown);

        if (name != "cards") { CloseReview(false); if (_cardEdit) ExitCardEdit(); }

        // the panel appears at once; its content is (re)built right after, so switching tabs never freezes the window
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (name == "planner") Safe(BuildPlanner);
            else if (name == "home") { _homeMsg = ""; Safe(BuildHome); }
            else if (name == "study") Safe(RefreshTopics);
            else if (name == "cards") Safe(RefreshCards);
            else if (name == "timetable") Safe(BuildTimetable);
            else if (name == "prayer") Safe(BuildPrayer);
            else if (name == "help") Safe(BuildHelp);
            else if (name == "exam") Safe(RefreshExams);
            Anim.Stagger(shown);
        }));
    }

    void ApplyTabs()
    {
        NavHelp.Visibility = Vis(Cfg.ShowHelp);
        if (NavHelp.IsChecked == true && !Cfg.ShowHelp) NavHome.IsChecked = true;
    }

    void Exit_Click(object sender, RoutedEventArgs e)
    {
        _exit = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        FlushCardEdits();
        if (Cfg.KeepRunning && !_exit)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        AudioService.Stop();
        App.MarkStarted();   // clean exit: the next start is a normal start
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    // ------------------------------------------------------------------ tables (boards)
    void LoadBoards()
    {
        _boards = Db.GetBoards();
        if (_boards.Count == 0)
        {
            Db.AddBoard("Main");
            _boards = Db.GetBoards();
        }
        if (!_boards.Any(b => b.Id == _boardId)) _boardId = _boards[0].Id;

        BoardsPanel.Children.Clear();
        foreach (var b in _boards)
        {
            var rb = new RadioButton
            {
                Content = b.Name,
                Tag = b.Id,
                GroupName = "boards",
                IsChecked = b.Id == _boardId,
                Style = UI.S("Chip")
            };
            rb.Checked += (s, e) =>
            {
                _boardId = (long)((RadioButton)s).Tag;
                RefreshItems();
            };
            // an alarm dragged from the list and dropped on this chip moves to this table
            rb.AllowDrop = true;
            rb.DragEnter += (s, e) => { if (e.Data.GetDataPresent("AlarmItem")) rb.Opacity = 0.6; };
            rb.DragLeave += (s, e) => rb.Opacity = 1;
            rb.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent("AlarmItem") ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            rb.Drop += (s, e) =>
            {
                rb.Opacity = 1;
                if (e.Data.GetData("AlarmItem") is not Item moved) return;
                long target = (long)((RadioButton)s).Tag;
                if (moved.BoardId != target) { Db.MoveItem(moved.Id, target); RefreshItems(); }
                e.Handled = true;
            };
            ChipBar.Attach(BoardsPanel, rb,
                (id, name) => { Db.RenameBoard(id, name); _boards = Db.GetBoards(); },
                ids => { Db.SetBoardOrder(ids); _boards = Db.GetBoards(); });
            BoardsPanel.Children.Add(rb);
        }

        // "+" is always the last chip
        var plus = UI.IconBtn("\uE710", NewBoard_Click, "new_table");
        plus.Margin = new Thickness(6, 3, 3, 3);
        BoardsPanel.Children.Add(plus);

        RefreshItems();
    }

    Point _itemDragStart;
    Item _itemDrag;

    void ItemsList_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _itemDrag = null;
        for (DependencyObject d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase || d is System.Windows.Controls.Primitives.ScrollBar) return;
            if (d is FrameworkElement fe && fe.DataContext is Item it) { _itemDrag = it; _itemDragStart = e.GetPosition(null); return; }
        }
    }

    void ItemsList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_itemDrag == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _itemDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _itemDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var it = _itemDrag; _itemDrag = null;
        DragDrop.DoDragDrop(ItemsList, new DataObject("AlarmItem", it), DragDropEffects.Move);
    }

    void RefreshItems()
    {
        var items = Db.GetItems(_boardId);
        ItemsList.ItemsSource = items;
        EmptyHint.Visibility = Vis(items.Count == 0);
    }

    void NewBoard_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, Loc.Get("table_name"), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        _boardId = Db.AddBoard(name);
        LoadBoards();
    }

    void RenameBoard_Click(object sender, RoutedEventArgs e)
    {
        var cur = _boards.FirstOrDefault(b => b.Id == _boardId);
        if (cur == null) return;
        var name = InputDialog.Ask(this, Loc.Get("table_name"), cur.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        Db.RenameBoard(cur.Id, name);
        LoadBoards();
    }

    void DeleteBoard_Click(object sender, RoutedEventArgs e)
    {
        if (_boards.Count <= 1) return;   // always keep at least one table
        var cur = _boards.FirstOrDefault(b => b.Id == _boardId);
        if (cur == null) return;
        if (!Dialogs.Confirm(this, string.Format(Loc.Get("confirm_del_table"), cur.Name))) return;
        Db.DeleteBoard(_boardId);
        LoadBoards();
    }

    // ------------------------------------------------------------------ items
    void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var it = new Item { BoardId = _boardId };
        if (new ItemDialog(this, it).ShowDialog() == true) RefreshItems();
    }

    void EditItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Item it) return;
        if (new ItemDialog(this, it).ShowDialog() == true) RefreshItems();
    }

    void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Item it) return;
        if (!Dialogs.Confirm(this, string.Format(Loc.Get("confirm_del_item"), it.Title))) return;
        Db.DeleteItem(it.Id);
        RefreshItems();
    }

    void TestItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Item it) Ring(it, true);
    }

    void ItemEnabled_Click(object sender, RoutedEventArgs e)
    {
        var cb = (CheckBox)sender;
        if (cb.DataContext is Item it)
        {
            bool on = cb.IsChecked == true;
            // Switching an alarm on whose time already passed: move it to its next occurrence. Otherwise the scheduler sees it as
            // "due" at once, treats it as missed and switches it straight back off (or fires it).
            if (on && it.AlarmAt.HasValue && it.AlarmAt.Value <= DateTime.Now)
            {
                var now = DateTime.Now;
                DateTime? nxt = it.Repeat == RepeatMode.None ? null : Scheduler.Next(it, now);
                if (nxt == null)
                {
                    var c = now.Date + it.AlarmAt.Value.TimeOfDay;
                    nxt = c > now ? c : c.AddDays(1);
                }
                Db.UpdateAlarm(it.Id, nxt, true);
                it.AlarmAt = nxt;
            }
            else Db.SetEnabled(it.Id, on);
            it.Enabled = on;
            // a click replaces the OneWay binding with a local value; restore it so recycled rows never keep a stale switch state
            System.Windows.Data.BindingOperations.SetBinding(cb, CheckBox.IsCheckedProperty,
                new System.Windows.Data.Binding("Enabled") { Mode = System.Windows.Data.BindingMode.OneWay });
            RefreshItems();
        }
    }

    // ------------------------------------------------------------------ alarms
    void OnFired(Item it)
    {
        Ring(it, true);
        RefreshItems();
    }

    void Ring(Item it, bool runTasks)
    {
        AudioService.Play(it.Sound, true);
        var pop = new AlarmPopup(it, minutes =>
        {
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMinutes(minutes) };
            tm.Tick += (s, e) => { tm.Stop(); Ring(it, false); };
            tm.Start();
        });
        pop.Show();
        if (runTasks) TaskRunner.RunAll(it.Tasks);
    }
}
