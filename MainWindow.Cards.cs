using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Hamyar;

// Flashcards: Leitner boxes G1..G5 + the 3D-style flip card review
public partial class MainWindow
{
    static readonly Random Rnd = new();

    bool _cardDueOnly = true;
    List<Card> _session = new();
    int _idx;
    bool _showBack;
    bool _practice;
    bool _animating;
    int _knew, _forgot;
    DateTime _shownAt, _revealNow;        // when the front appeared / when the answer was revealed
    double? _thinkSeconds;                // seconds between the two (null until the answer is revealed)
    double _speed = 1.0;                  // speed factor derived from that time
    bool _paused;                         // timer paused: the card is hidden and has been swapped for a later one
    bool _autoForgot;                     // the 30 s ran out: answer shown, card already recorded as Forgot, waiting for "Next"
    System.Windows.Threading.DispatcherTimer _answerTimer;

    // ------------------------------------------------------------------ list
    void CardCourse_Checked(object sender, RoutedEventArgs e)
    {
        _cardCourseId = (long)((RadioButton)sender).Tag;
        _cardSubjectId = 0;                       // subjects belong to one course: back to its "All ..." chip
        LoadCardSubjects();
        RefreshCards();
    }

    void CardFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _cardDueOnly = (string)((RadioButton)sender).Tag == "due";
        RefreshCards();
    }

    static ScrollViewer FindScroll(DependencyObject root)
    {
        if (root == null) return null;
        if (root is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScroll(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    void RefreshCards()
    {
        if (CardsList == null) return;
        if (_cardEdit)
        {
            // quick-edit is open: the course / subject changed, so its lines are rebuilt (pending edits are saved first)
            BuildEditRows();
            UpdateBadges();
            return;
        }
        var cards = Db.GetCards(_cardCourseId, _cardSubjectId);
        if (_cardDueOnly) cards = cards.Where(c => c.IsDue).ToList();

        // the list is replaced, but the user keeps their scroll position (the once-a-minute refresh must not jump to the top)
        var sv = FindScroll(CardsList);
        double offset = sv?.VerticalOffset ?? 0;
        CardsList.ItemsSource = cards;
        if (sv != null && offset > 0)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => sv.ScrollToVerticalOffset(offset)));

        UpdateBadges();      // reads the two due-counts once; the summary below reuses them
        CardsSummary.Text = string.Format(Loc.Get("card_summary"), _dueCardCount);
        var windowReason = StudyWindow.Check(DateTime.Now);
        if (windowReason != StudyWindow.Reason.Open)
            CardsSummary.Text += "   ·   " + StudyWindow.ReasonText(windowReason);

        string hint = null;
        if (_cardCourses.Count == 0) hint = "card_empty_courses";
        else if (cards.Count == 0) hint = _cardDueOnly ? "card_empty_due" : "card_empty_all";
        CardsEmptyHint.Text = hint == null ? "" : Loc.Get(hint);
        CardsEmptyHint.Visibility = Vis(hint != null);
    }

    void AddCard_Click(object sender, RoutedEventArgs e)
    {
        if (_cardEdit) { AddEditRow(); return; }      // quick edit: the same "new card" button adds a line after the selected card
        if (_cardCourses.Count == 0)
        {
            NewCardCourse_Click(null, null);         // a card needs a course: ask for one first
            if (_cardCourses.Count == 0) return;
        }
        var c = new Card();
        if (new CardDialog(this, c, _cardCourses, _cardCourseId, _cardSubjectId).ShowDialog() == true) RefreshCards();
    }

    /// <summary>Double-click a card to edit it.</summary>
    void CardRow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
        if (((FrameworkElement)sender).DataContext is not Card c) return;
        if (new CardDialog(this, c, _cardCourses, _cardCourseId, _cardSubjectId).ShowDialog() == true) RefreshCards();
        e.Handled = true;
    }

    static T FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (; d != null; d = VisualTreeHelper.GetParent(d) as DependencyObject) if (d is T t) return t;
        return null;
    }

    /// <summary>The "..." menu: everything that is not needed every day.</summary>
    void CardsMore_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, FlowDirection = Loc.I.Flow };
        void Add(string key, RoutedEventHandler h, bool enabled = true)
        {
            var mi = new MenuItem { Header = Loc.Get(key), IsEnabled = enabled };
            mi.Click += h;
            menu.Items.Add(mi);
        }
        bool course = _cardCourseId != 0, subject = course && _cardSubjectId != 0;
        Add("card_practice", PracticeAll_Click);
        Add("card_course_settings", CourseSettings_Click);
        if (subject) Add("subj_rename", RenameCardSubject_Click);
        menu.Items.Add(new Separator());
        Add("card_import_csv", ImportCsv_Click);
        Add("card_export_csv", ExportCsv_Click);
        menu.Items.Add(new Separator());
        if (subject) Add("subj_delete", DeleteCardSubject_Click);
        Add("card_delete_course", DeleteCardCourse_Click, course);
        menu.IsOpen = true;
    }

    void EditCard_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Card c) return;
        if (new CardDialog(this, c, _cardCourses, _cardCourseId, _cardSubjectId).ShowDialog() == true) RefreshCards();
    }

    void DeleteCard_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Card c) return;
        if (!Dialogs.Confirm(this, Loc.Get("confirm_del_card"))) return;
        Db.DeleteCard(c.Id);
        RefreshCards();
    }

    // ------------------------------------------------------------------ review session
    void StartReview_Click(object sender, RoutedEventArgs e) => StartReview(false);
    void PracticeAll_Click(object sender, RoutedEventArgs e) => StartReview(true);

    void StartReview(bool practice)
    {
        if (_cardEdit) ExitCardEdit();
        var pool = Db.GetCards(_cardCourseId, _cardSubjectId);
        var list = practice ? pool.OrderBy(c => Rnd.Next()).ToList() : pool.Where(c => c.IsDue).ToList();
        if (list.Count == 0)
        {
            Dialogs.Info(this, Loc.Get(practice ? "card_none" : "card_none_due"));
            return;
        }
        _session = list;
        _idx = 0;
        _knew = 0;
        _forgot = 0;
        _practice = practice;
        CardsMain.Visibility = Visibility.Collapsed;
        ReviewPane.Visibility = Visibility.Visible;
        ShowCurrent();
    }

    void CloseReview_Click(object sender, RoutedEventArgs e) => CloseReview(true);

    void CloseReview(bool refresh)
    {
        if (ReviewPane == null || ReviewPane.Visibility != Visibility.Visible) return;
        StopAnswerTimer();
        _paused = false;
        ReviewPane.Visibility = Visibility.Collapsed;
        CardsMain.Visibility = Visibility.Visible;
        _animating = false;
        if (refresh) RefreshCards();
    }

    static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Colors + texts of the card for the side that is currently up: the front is the vivid accent
    /// gradient, the back is a softer tint of that SAME accent (not a fixed, unrelated color) mixed into the
    /// theme's own dark/light base - so the back always follows whichever accent color the user picked.</summary>
    void ApplyFace()
    {
        var acc = ThemeManager.Accents[Cfg.Accent];
        bool dark = Cfg.Theme == 0;
        bool yellowAccent = Cfg.Accent == 3;   // yellow needs dark text for contrast; every other accent needs white/light text
        Brush text;
        if (!_showBack)
        {
            FlipCard.Background = new LinearGradientBrush(acc, Darken(acc, yellowAccent ? 0.78 : 0.5), 45.0);
            text = yellowAccent ? Brushes.Black : Brushes.White;
        }
        else if (dark)
        {
            // dark theme: the accent tinted into a near-black base, so it stays a readable, low-contrast back
            // face while visibly carrying the chosen accent's hue.
            var c1 = Mix(Color.FromRgb(0x14, 0x18, 0x22), acc, 0.22);
            var c2 = Mix(Color.FromRgb(0x24, 0x2A, 0x3A), acc, 0.30);
            FlipCard.Background = new LinearGradientBrush(c1, c2, 45.0);
            text = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
        }
        else
        {
            // light theme: the accent tinted into a near-white base, same idea the other way around.
            var c1 = Mix(Colors.White, acc, 0.12);
            var c2 = Mix(Color.FromRgb(0xE4, 0xE9, 0xF5), acc, 0.22);
            FlipCard.Background = new LinearGradientBrush(c1, c2, 45.0);
            text = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
        }
        FaceText.Foreground = text;
        FaceLabel.Foreground = text;
        FaceHint.Foreground = text;

        if (_idx < _session.Count)
        {
            var c = _session[_idx];
            FaceLabel.Text = Loc.Get(_showBack ? "card_a" : "card_q");
            var shown = _showBack ? c.Back : c.Front;
            FaceText.Text = string.IsNullOrWhiteSpace(shown) ? "—" : shown;
            FaceHint.Text = _showBack ? "" : Loc.Get("card_tap");
        }
    }

    void ResetCardTransform()
    {
        FlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FlipScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        FlipScale.ScaleX = 1;
        FlipScale.ScaleY = 1;
    }

    void ShowCurrent()
    {
        _showBack = false;
        _animating = false;
        ResetCardTransform();

        if (_idx >= _session.Count)
        {
            ShowSessionDone();
            return;
        }
        var c = _session[_idx];
        ReviewProgress.Text = string.Format(Loc.Get("card_progress"), _idx + 1, _session.Count);
        ReviewBoxText.Text = c.BoxText;
        ReviewBoxBadge.Visibility = Visibility.Visible;
        ApplyFace();
        _shownAt = DateTime.Now;
        _thinkSeconds = null;
        _autoForgot = false;
        _paused = false;
        var showText = new TextBlock();
        UI.Bind(showText, TextBlock.TextProperty, "card_show");
        BtnShow.Content = showText;
        BtnShow.Visibility = Visibility.Visible;
        SetAnswerButtonsVisible(false);
        ReviewKeys.Text = Loc.Get("card_keys");
        UpdatePauseButton();
        StartAnswerTimer();
    }

    void ShowSessionDone()
    {
        ApplyFace();                                   // front colors
        FaceLabel.Text = "";
        FaceText.Text = string.Format(Loc.Get("card_done"), _knew, _forgot);
        FaceHint.Text = "";
        ReviewProgress.Text = "";
        StopAnswerTimer();
        _paused = false;
        UpdatePauseButton();
        ReviewBoxBadge.Visibility = Visibility.Collapsed;
        BtnShow.Visibility = Visibility.Collapsed;
        SetAnswerButtonsVisible(false);
        ReviewKeys.Text = "";
    }

    /// <summary>3D-style flip: the card squeezes to a line while tilting, the face is swapped, then it opens again.</summary>
    void FlipCardNow()
    {
        if (_animating || _paused || _idx >= _session.Count) return;
        _animating = true;

        if (!_showBack && _thinkSeconds == null)
        {
            // the moment the answer is revealed fixes the answer time; the interval previews and the real schedule both use it
            _revealNow = DateTime.Now;
            _thinkSeconds = (_revealNow - _shownAt).TotalSeconds;
            _speed = ReviewEngine.SpeedFactor(_thinkSeconds.Value);
            UpdatePauseButton();
        }

        var half = TimeSpan.FromMilliseconds(170);
        var close = new DoubleAnimation(1, 0, half) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        close.Completed += (s, e) =>
        {
            _showBack = !_showBack;
            ApplyFace();
            BtnShow.Visibility = Vis(!_showBack || _autoForgot);
            SetAnswerButtonsVisible(_showBack && !_autoForgot);
            if (_showBack && !_autoForgot) UpdateAnswerButtons();

            var open = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            open.Completed += (s2, e2) =>
            {
                ResetCardTransform();
                _animating = false;
            };
            FlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, open);
            FlipScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(200)));
        };
        FlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, close);
        FlipScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.92, half));
    }

    // ----- 30-second answer timer: pressing "Show answer" faster gives longer gaps; running out counts as Forgot -----
    void StartAnswerTimer()
    {
        if (_answerTimer == null)
        {
            _answerTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _answerTimer.Tick += (s, e) => AnswerTimerTick();
        }
        _answerTimer.Start();
        AnswerTimerTick();
    }

    void StopAnswerTimer()
    {
        _answerTimer?.Stop();
        if (ReviewTimer != null) ReviewTimer.Text = "";
    }

    void AnswerTimerTick()
    {
        if (ReviewPane == null || ReviewPane.Visibility != Visibility.Visible || _idx >= _session.Count) { StopAnswerTimer(); return; }
        if (_paused) return;
        if (_thinkSeconds != null) { ReviewTimer.Text = ""; return; }        // answer already revealed: nothing to count down
        if (_animating) return;

        double left = ReviewEngine.AnswerTimeLimitSeconds - (DateTime.Now - _shownAt).TotalSeconds;
        if (left <= 0) { TimeUp(); return; }
        int shown = (int)Math.Ceiling(left);
        ReviewTimer.Text = string.Format(Loc.Get("card_timer"), shown);
        if (shown <= 5) ReviewTimer.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
        else ReviewTimer.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
    }

    /// <summary>The 30 seconds ran out: show the answer and record the card as Forgot right away.</summary>
    void TimeUp()
    {
        if (_idx >= _session.Count || _showBack || _animating || _thinkSeconds != null) return;
        var c = _session[_idx];
        _autoForgot = true;
        _revealNow = DateTime.Now;
        _thinkSeconds = ReviewEngine.AnswerTimeLimitSeconds;
        _speed = ReviewEngine.SpeedFactor(_thinkSeconds.Value);
        ReviewTimer.Text = "";
        UpdatePauseButton();
        _forgot++;

        string note;
        if (_practice) note = Loc.Get("card_timeout_practice");
        else
        {
            Leitner.Rate(c, Grade.Again, _revealNow, ExamById(c.ExamId), _speed);
            Db.SaveCard(c);
            note = string.Format(Loc.Get("card_timeout"), ReviewEngine.FormatInterval(c.NextReview - _revealNow));
        }

        var next = new TextBlock { Text = Loc.Get("card_next") };
        BtnShow.Content = next;
        FlipCardNow();
        ReviewKeys.Text = note;
    }

    void ContinueAfterTimeout()
    {
        if (_animating || !_autoForgot) return;
        _autoForgot = false;
        _idx++;
        ShowCurrent();
    }

    // ----- pause: resets the timer, hides the card, and swaps it for a later one so it is not the next one shown -----
    void Pause_Click(object sender, RoutedEventArgs e) => TogglePause();

    void TogglePause()
    {
        if (ReviewPane == null || ReviewPane.Visibility != Visibility.Visible || _idx >= _session.Count) return;

        if (_paused)
        {
            ShowCurrent();                       // shows the (new) current card and restarts the 30 s
            return;
        }
        if (_animating || _autoForgot || _thinkSeconds != null) return;    // only while a card is waiting for its answer

        _paused = true;
        StopAnswerTimer();
        if (_idx + 1 < _session.Count)
        {
            // another card is waiting in this session: swap, so the paused one comes back later
            int j = Rnd.Next(_idx + 1, _session.Count);
            (_session[_idx], _session[j]) = (_session[j], _session[_idx]);
        }
        else
        {
            // the paused card is the last one left: bring in a different card from the deck to show next, and keep this one after it
            var inSession = new HashSet<long>(_session.Select(k => k.Id));
            var others = Db.GetCards(_cardCourseId, _cardSubjectId).Where(k => !inSession.Contains(k.Id)).ToList();
            if (others.Count > 0) _session.Insert(_idx, others[Rnd.Next(others.Count)]);
        }
        ShowPaused();
    }

    void ShowPaused()
    {
        ApplyFace();                              // front colors; the texts are replaced right below, so nothing of any card shows
        FaceLabel.Text = "";
        FaceText.Text = Loc.Get("card_paused");
        FaceHint.Text = Loc.Get("card_paused_hint");
        ReviewBoxBadge.Visibility = Visibility.Collapsed;
        BtnShow.Visibility = Visibility.Collapsed;
        SetAnswerButtonsVisible(false);
        ReviewKeys.Text = "";
        UpdatePauseButton();
    }

    void UpdatePauseButton()
    {
        if (BtnPause == null) return;
        bool inSession = ReviewPane.Visibility == Visibility.Visible && _idx < _session.Count;
        BtnPause.IsEnabled = inSession && (_paused || (_thinkSeconds == null && !_autoForgot));
        PauseIcon.Text = _paused ? "\uE768" : "\uE769";                 // play / pause glyphs
        BtnPause.ToolTip = Loc.Get(_paused ? "card_resume" : "card_pause");
    }

    void SetAnswerButtonsVisible(bool on)
    {
        BtnAgain.Visibility = BtnHard.Visibility = BtnGood.Visibility = BtnEasy.Visibility = Vis(on);
    }

    /// <summary>Writes "label + when it will come back" on each of the four buttons. The time comes from running the
    /// real scheduler on a copy of the card, so what a button says is exactly what pressing it does.</summary>
    void UpdateAnswerButtons()
    {
        if (_idx >= _session.Count) return;
        var c = _session[_idx];
        var exam = ExamById(c.ExamId);

        string When(Grade g)
        {
            if (_practice) return "";                       // practice mode never changes the schedule
            var copy = c.CloneForPreview();
            Leitner.Rate(copy, g, _revealNow, exam, _speed);
            return ReviewEngine.FormatInterval(copy.NextReview - _revealNow);
        }

        SetAnswerButton(BtnEasy, "card_easy", When(Grade.Easy));
        SetAnswerButton(BtnGood, "card_medium", When(Grade.Good));
        SetAnswerButton(BtnHard, "card_hard", When(Grade.Hard));
        SetAnswerButton(BtnAgain, "card_forgot", When(Grade.Again));
    }

    /// <summary>Anki-style button: the interval (1d, 5h, 10m, 1mo) on top, the word under it.</summary>
    static void SetAnswerButton(Button b, string labelKey, string when)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (when.Length > 0)
            sp.Children.Add(new TextBlock { Text = when, FontSize = 13, FontWeight = FontWeights.Bold, FlowDirection = System.Windows.FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = Loc.Get(labelKey), FontSize = 12.5, Opacity = 0.92, Margin = new Thickness(0, when.Length > 0 ? 2 : 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        b.Content = sp;
    }

    void Answer(Grade grade)
    {
        if (_animating || _paused || _autoForgot || _idx >= _session.Count || !_showBack) return;
        var c = _session[_idx];
        if (grade == Grade.Again) _forgot++; else _knew++;
        if (!_practice)
        {
            // _revealNow keeps the real schedule identical to the time shown on the button. Not .Today: intervals can be minutes/hours.
            Leitner.Rate(c, grade, _revealNow, ExamById(c.ExamId), _speed);
            Db.SaveCard(c);
        }
        _idx++;
        ShowCurrent();
    }

    void ShowAnswer_Click(object sender, RoutedEventArgs e)
    {
        if (_autoForgot) { ContinueAfterTimeout(); return; }
        FlipCardNow();
    }
    void Easy_Click(object sender, RoutedEventArgs e) => Answer(Grade.Easy);
    void Good_Click(object sender, RoutedEventArgs e) => Answer(Grade.Good);
    void Hard_Click(object sender, RoutedEventArgs e) => Answer(Grade.Hard);
    void Again_Click(object sender, RoutedEventArgs e) => Answer(Grade.Again);

    void FlipCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_idx >= _session.Count) { CloseReview(true); return; }
        if (_paused) { TogglePause(); return; }
        if (_autoForgot) { ContinueAfterTimeout(); return; }
        FlipCardNow();
    }

    // hover: the card lifts a little (bigger + deeper shadow). It only ever gets scaled, so it stays a perfect rectangle.
    void FlipCard_MouseEnter(object sender, MouseEventArgs e) => AnimateHover(true);
    void FlipCard_MouseLeave(object sender, MouseEventArgs e) => AnimateHover(false);

    void AnimateHover(bool on)
    {
        var d = TimeSpan.FromMilliseconds(160);
        double scale = on ? 1.035 : 1.0;
        HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, d));
        HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, d));
        CardShadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(on ? 24 : 16, d));
        CardShadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(on ? 58 : 42, d));
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ReviewPane == null || ReviewPane.Visibility != Visibility.Visible) { TryHotkey(e); return; }
        if (e.Key == Key.Space) { if (_idx >= _session.Count) CloseReview(true); else if (_paused) TogglePause(); else if (_autoForgot) ContinueAfterTimeout(); else FlipCardNow(); e.Handled = true; }
        else if (e.Key == Key.P) { TogglePause(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseReview(true); e.Handled = true; }
        else if (_showBack && (e.Key == Key.D1 || e.Key == Key.NumPad1)) { Answer(Grade.Easy); e.Handled = true; }
        else if (_showBack && (e.Key == Key.D2 || e.Key == Key.NumPad2 || e.Key == Key.Right)) { Answer(Grade.Good); e.Handled = true; }
        else if (_showBack && (e.Key == Key.D3 || e.Key == Key.NumPad3)) { Answer(Grade.Hard); e.Handled = true; }
        else if (_showBack && (e.Key == Key.D4 || e.Key == Key.NumPad4 || e.Key == Key.Left)) { Answer(Grade.Again); e.Handled = true; }
    }
}
