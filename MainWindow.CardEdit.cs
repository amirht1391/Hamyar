using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hamyar;

/// <summary>One editable line of the quick-edit list: the two texts are bound straight to the boxes.</summary>
public class CardRow : INotifyPropertyChanged
{
    public Card Card { get; }
    public bool Dirty { get; set; }
    public event PropertyChangedEventHandler PropertyChanged;
    public event Action<CardRow> Edited;

    public CardRow(Card c) { Card = c; }

    public string Front
    {
        get => Card.Front;
        set { if (Card.Front == value) return; Card.Front = value; Dirty = true; Edited?.Invoke(this); }
    }

    public string Back
    {
        get => Card.Back;
        set { if (Card.Back == value) return; Card.Back = value; Dirty = true; Edited?.Invoke(this); }
    }

    bool _sel;
    public bool IsSelected
    {
        get => _sel;
        set { if (_sel == value) return; _sel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public string SubjectText => string.IsNullOrEmpty(Card.SubjectName) ? "—" : Card.SubjectName;
    public void RefreshSubject() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubjectText)));
}

// Flashcards "quick edit": every card of the chosen course / subject is an editable line. Typing saves by itself
// (batched, one transaction), cards can be added, ticked and moved / deleted / re-dated in bulk.
public partial class MainWindow
{
    bool _cardEdit;
    readonly ObservableCollection<CardRow> _editRows = new();
    DispatcherTimer _editSaveTimer;

    void ToggleCardEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_cardEdit) ExitCardEdit(); else EnterCardEdit();
    }

    void EnterCardEdit()
    {
        if (_cardCourses.Count == 0) { Dialogs.Info(this, Loc.Get("card_empty_courses")); return; }
        _cardEdit = true;
        CardsList.Visibility = Visibility.Collapsed;
        CardsEditHost.Visibility = Visibility.Visible;
        CardsEditBar.Visibility = Visibility.Visible;
        CardsFilterRow.Visibility = Visibility.Collapsed;
        BuildEditRows();
    }

    void ExitCardEdit()
    {
        if (!_cardEdit) return;
        FlushCardEdits();
        // brand-new cards that were left completely empty are not kept
        var blank = _editRows.Where(r => string.IsNullOrWhiteSpace(r.Card.Front) && string.IsNullOrWhiteSpace(r.Card.Back) && r.Card.Reviews == 0)
                             .Select(r => r.Card.Id).ToList();
        if (blank.Count > 0) Db.DeleteCards(blank);
        _cardEdit = false;
        _buildToken++;
        _editRows.Clear();
        CardsEditHost.Visibility = Visibility.Collapsed;
        CardsEditBar.Visibility = Visibility.Collapsed;
        CardsFilterRow.Visibility = Visibility.Visible;
        CardsList.Visibility = Visibility.Visible;
        RefreshCards();
    }

    int _buildToken;

    void BuildEditRows()
    {
        FlushCardEdits();
        _editRows.Clear();
        var cards = Db.GetCards(_cardCourseId, _cardSubjectId, true);
        // many rows: virtualize; otherwise no virtualization, so Tab always reaches the next box
        bool big = cards.Count > 300;
        CardsEditList.SetValue(VirtualizingPanel.IsVirtualizingProperty, big);
        CardsEditList.SetValue(ScrollViewer.CanContentScrollProperty, big);
        CardsEditList.ItemsSource = _editRows;

        // the rows are heavy to build: show the first screenful at once, add the rest in small background batches
        int token = ++_buildToken, next = 0;
        const int First = 12, Batch = 20;
        void AddUpTo(int count)
        {
            for (; next < cards.Count && next < count; next++)
            {
                var row = new CardRow(cards[next]);
                row.Edited += OnRowEdited;
                _editRows.Add(row);
            }
        }
        AddUpTo(First);
        UpdateEditSummary();
        void More()
        {
            if (token != _buildToken || !_cardEdit || next >= cards.Count) return;
            AddUpTo(next + Batch);
            UpdateEditSummary();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(More));
        }
        if (next < cards.Count) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(More));
    }

    void OnRowEdited(CardRow row)
    {
        if (_editSaveTimer == null)
        {
            _editSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _editSaveTimer.Tick += (s, a) => FlushCardEdits();
        }
        _editSaveTimer.Stop();
        _editSaveTimer.Start();            // saves 0.7 s after the last keystroke
    }

    /// <summary>Writes every changed line in ONE transaction. Safe to call any time (tab switch, closing, a refresh).</summary>
    public void FlushCardEdits()
    {
        _editSaveTimer?.Stop();
        var dirty = _editRows.Where(r => r.Dirty).ToList();
        if (dirty.Count == 0) return;
        try
        {
            Db.UpdateCardTexts(dirty.Select(r => r.Card));
            foreach (var r in dirty) r.Dirty = false;
        }
        catch (Exception ex) { Log.Write("Card quick-edit save: " + ex.Message); }
    }

    void UpdateEditSummary()
    {
        int sel = _editRows.Count(r => r.IsSelected);
        CardsEditHint.Text = Loc.Get("card_edit_hint") + (sel > 0 ? "   ·   " + string.Format(Loc.Get("card_sel_count"), sel) : "");
    }

    // ---------------------------------------------------------------- row actions
    void AddCardRow_Click(object sender, RoutedEventArgs e) => AddEditRow();

    /// <summary>The row new cards are added after: the last ticked line, else the line that has the cursor in it.</summary>
    CardRow _focusRow;

    CardRow AnchorRow() =>
        _editRows.LastOrDefault(r => r.IsSelected) ?? (_focusRow != null && _editRows.Contains(_focusRow) ? _focusRow : null);

    void EditList_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        for (var d = e.NewFocus as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d) as DependencyObject ?? LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement fe && fe.DataContext is CardRow r) { _focusRow = r; return; }
    }

    void AddEditRow()
    {
        if (!_cardEdit) return;
        FlushCardEdits();
        var anchor = AnchorRow();
        // the new card goes in the same course / subject as the card it follows
        var course = _cardCourses.FirstOrDefault(c => c.Id == (anchor?.Card.CourseId ?? _cardCourseId)) ?? _cardCourses[0];
        long subjectId = anchor != null ? anchor.Card.SubjectId : (_cardCourseId != 0 ? _cardSubjectId : 0);
        var sj = Db.GetSubjects(course.Id).FirstOrDefault(x => x.Id == subjectId);
        var card = new Card { CourseId = course.Id, CourseName = course.Name, SubjectId = sj?.Id ?? 0, StartDate = sj?.StartDate.Date ?? DateTime.Today };
        Leitner.Restart(card);
        ReviewEngine.Reschedule(card, ExamById(course.ExamId), DateTime.Now);
        Db.SaveCard(card);
        card.SubjectName = sj?.Name ?? "";

        var row = new CardRow(card);
        row.Edited += OnRowEdited;
        int at = anchor != null ? _editRows.IndexOf(anchor) + 1 : _editRows.Count;
        _editRows.Insert(at, row);
        PersistOrder();
        CardsEditList.ScrollIntoView(row);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => FocusRowFront(row)));
    }

    void PersistOrder()
    {
        try { Db.SaveCardOrder(_editRows.Select(r => r.Card).ToList()); }
        catch (Exception ex) { Log.Write("Card order save: " + ex.Message); }
    }

    // ---------------------------------------------------------------- drag to reorder (grab the ≡ handle)
    // A "ghost" copy of the line follows the mouse, the other lines slide out of the way with a short animation,
    // and the order is committed when the mouse is released.
    sealed class DragGhost : System.Windows.Documents.Adorner
    {
        readonly VisualBrush _brush;
        readonly Size _size;
        public double Top;
        public DragGhost(UIElement host, FrameworkElement item) : base(host)
        {
            IsHitTestVisible = false;
            _size = new Size(item.ActualWidth, item.ActualHeight);
            _brush = new VisualBrush(item) { Stretch = Stretch.Fill };
        }
        protected override void OnRender(DrawingContext dc)
        {
            var r = new Rect(0, Top, _size.Width, _size.Height);
            // soft shadow
            for (int i = 6; i >= 1; i--)
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(10 + (6 - i) * 3), 0, 0, 0)), null,
                    new Rect(r.X - i + 2, r.Y - i + 7, r.Width + 2 * i - 4, r.Height + 2 * i - 4), 14 + i, 14 + i);
            dc.PushOpacity(0.95);
            dc.DrawRectangle(_brush, null, r);
            dc.Pop();
        }
    }

    CardRow _dragRow;
    Point _dragDown;
    bool _dragging;
    double _grabY, _mouseY;
    DragGhost _ghost;
    System.Windows.Documents.AdornerLayer _ghostLayer;
    FrameworkElement _dragItem;
    DispatcherTimer _autoScroll;
    int _dragFrom, _dragTo;

    void DragHandle_Down(object sender, MouseButtonEventArgs e)
    {
        _dragRow = ((FrameworkElement)sender).DataContext as CardRow;
        _dragDown = e.GetPosition(CardsEditList);
        _dragging = false;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    FrameworkElement ItemOf(CardRow r) => CardsEditList.ItemContainerGenerator.ContainerFromItem(r) as FrameworkElement;

    void DragHandle_Move(object sender, MouseEventArgs e)
    {
        if (_dragRow == null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(CardsEditList);
        _mouseY = p.Y;
        if (!_dragging)
        {
            if (Math.Abs(p.Y - _dragDown.Y) < 4) return;
            if (!BeginDrag()) { _dragRow = null; return; }
        }
        UpdateDrag();
    }

    bool BeginDrag()
    {
        _dragItem = ItemOf(_dragRow);
        _ghostLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(CardsEditList);
        if (_dragItem == null || _ghostLayer == null) return false;
        FlushCardEdits();
        _dragging = true;
        _dragFrom = _dragTo = _editRows.IndexOf(_dragRow);
        _grabY = _dragDown.Y - _dragItem.TranslatePoint(new Point(0, 0), CardsEditList).Y;
        _ghost = new DragGhost(CardsEditList, _dragItem);
        _ghostLayer.Add(_ghost);
        _dragItem.Opacity = 0.25;
        Mouse.OverrideCursor = Cursors.SizeAll;
        _autoScroll ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _autoScroll.Tick -= AutoScroll_Tick;
        _autoScroll.Tick += AutoScroll_Tick;
        _autoScroll.Start();
        return true;
    }

    void AutoScroll_Tick(object s, EventArgs a)
    {
        if (!_dragging) return;
        var sv = FindFirst<ScrollViewer>(CardsEditList);
        if (sv == null) return;
        double h = CardsEditList.ActualHeight, edge = 48, v = 0;
        if (_mouseY < edge) v = -(edge - Math.Max(_mouseY, 0)) / edge;
        else if (_mouseY > h - edge) v = (Math.Min(_mouseY, h) - (h - edge)) / edge;
        if (v == 0) return;
        double step = v * 22;
        if (sv.CanContentScroll) sv.ScrollToVerticalOffset(sv.VerticalOffset + Math.Sign(step));   // virtualized: one line
        else sv.ScrollToVerticalOffset(sv.VerticalOffset + step);
        UpdateDrag();
    }

    static double CurrentY(FrameworkElement el) => el.RenderTransform is TranslateTransform t ? t.Y : 0;

    void UpdateDrag()
    {
        if (!_dragging) return;
        double itemH = _dragItem.ActualHeight;
        double top = Math.Max(0, Math.Min(CardsEditList.ActualHeight - itemH, _mouseY - _grabY));
        _ghost.Top = top;
        _ghost.InvalidateVisual();
        double center = top + itemH / 2;

        // slot: the first other line (in list order) whose centre lies below the ghost's centre
        int target = -1;
        var realized = new List<(int idx, FrameworkElement el, double mid)>();
        for (int i = 0; i < _editRows.Count; i++)
        {
            if (i == _dragFrom) continue;
            if (ItemOf(_editRows[i]) is not FrameworkElement el || !el.IsVisible) continue;
            double y = el.TranslatePoint(new Point(0, 0), CardsEditList).Y - CurrentY(el);
            realized.Add((i, el, y + el.ActualHeight / 2));
        }
        foreach (var r in realized) if (r.mid > center) { target = r.idx; break; }
        int to = target == -1 ? (realized.Count > 0 ? realized[^1].idx : _dragFrom) : (target > _dragFrom ? target - 1 : target);
        if (to < 0) to = 0;
        _dragTo = to;

        foreach (var r in realized)
        {
            double want = 0;
            if (_dragTo > _dragFrom && r.idx > _dragFrom && r.idx <= _dragTo) want = -itemH;
            else if (_dragTo < _dragFrom && r.idx >= _dragTo && r.idx < _dragFrom) want = itemH;
            SlideTo(r.el, want);
        }
    }

    static void SlideTo(FrameworkElement el, double y)
    {
        if (el.RenderTransform is not TranslateTransform t || t.IsFrozen) { t = new TranslateTransform(); el.RenderTransform = t; }
        if (el.Tag is double prev && Math.Abs(prev - y) < 0.5) return;       // already heading there
        el.Tag = y;
        t.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(y, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
    }

    void DragHandle_Up(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).ReleaseMouseCapture();     // LostMouseCapture finishes the drag
    }

    void DragHandle_Lost(object sender, MouseEventArgs e)
    {
        var moving = _dragRow; bool was = _dragging;
        _dragRow = null; _dragging = false;
        if (!was) return;
        _autoScroll?.Stop();
        Mouse.OverrideCursor = null;
        _ghostLayer?.Remove(_ghost);
        _ghost = null;
        // clear every slide transform, then apply the new order
        for (int i = 0; i < _editRows.Count; i++)
            if (ItemOf(_editRows[i]) is FrameworkElement el)
            {
                if (el.RenderTransform is TranslateTransform t && !t.IsFrozen) t.BeginAnimation(TranslateTransform.YProperty, null);
                el.RenderTransform = Transform.Identity; el.Tag = null; el.Opacity = 1;
            }
        if (_dragItem != null) _dragItem.Opacity = 1;
        if (moving != null && _dragTo != _dragFrom && _dragFrom >= 0)
        {
            _editRows.Move(_dragFrom, _dragTo);
            PersistOrder();
            CardsEditList.ScrollIntoView(moving);
        }
    }

    void FocusRowFront(CardRow row)
    {
        if (CardsEditList.ItemContainerGenerator.ContainerFromItem(row) is not DependencyObject cont) return;
        var tb = FindFirst<TextBox>(cont);
        tb?.Focus();
    }

    static T FindFirst<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var ch = VisualTreeHelper.GetChild(root, i);
            if (ch is T t) return t;
            var deep = FindFirst<T>(ch);
            if (deep != null) return deep;
        }
        return null;
    }

    void EditList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            AddEditRow();
            e.Handled = true;
        }
    }

    void RowSelect_Changed(object sender, RoutedEventArgs e) => UpdateEditSummary();

    void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not CardRow row) return;
        if (!Dialogs.Confirm(this, Loc.Get("confirm_del_card"))) return;
        Db.DeleteCard(row.Card.Id);
        _editRows.Remove(row);
        UpdateEditSummary();
    }

    /// <summary>Subject of a single line: a small menu with the subjects of that card's course.</summary>
    void RowSubject_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        if (btn.DataContext is not CardRow row) return;
        ShowSubjectMenu(btn, row.Card.CourseId, id =>
        {
            row.Card.SubjectId = id;
            row.Card.SubjectName = Db.GetSubjects(row.Card.CourseId).FirstOrDefault(x => x.Id == id)?.Name ?? "";
            row.RefreshSubject();
            Db.UpdateCardTexts(new[] { row.Card });
            ApplySubjectStart(new[] { row.Card });
        });
    }

    void ShowSubjectMenu(FrameworkElement anchor, long courseId, Action<long> pick)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, MaxHeight = 320 };
        menu.FlowDirection = Loc.I.Flow;
        var none = new MenuItem { Header = Loc.Get("subj_none") };
        none.Click += (s, a) => pick(0);
        menu.Items.Add(none);
        foreach (var sj in Db.GetSubjects(courseId))
        {
            var id = sj.Id;
            var mi = new MenuItem { Header = sj.Name };
            mi.Click += (s, a) => pick(id);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- bulk actions on ticked lines
    void SelectAllRows_Click(object sender, RoutedEventArgs e)
    {
        bool all = _editRows.Count > 0 && _editRows.All(r => r.IsSelected);
        foreach (var r in _editRows) r.IsSelected = !all;
        UpdateEditSummary();
    }

    List<CardRow> Selected()
    {
        var sel = _editRows.Where(r => r.IsSelected).ToList();
        if (sel.Count == 0) Dialogs.Info(this, Loc.Get("card_pick_selected"));
        return sel;
    }

    void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected();
        if (sel.Count == 0) return;
        if (!Dialogs.Confirm(this, string.Format(Loc.Get("confirm_del_cards"), sel.Count))) return;
        FlushCardEdits();
        Db.DeleteCards(sel.Select(r => r.Card.Id));
        foreach (var r in sel) _editRows.Remove(r);
        UpdateEditSummary();
    }

    void BulkMove_Click(object sender, RoutedEventArgs e)
    {
        if (_cardCourseId == 0) { Dialogs.Info(this, Loc.Get("card_pick_course_first")); return; }
        var sel = Selected();
        if (sel.Count == 0) return;
        ShowSubjectMenu((FrameworkElement)sender, _cardCourseId, id =>
        {
            var name = Db.GetSubjects(_cardCourseId).FirstOrDefault(x => x.Id == id)?.Name ?? "";
            foreach (var r in sel) { r.Card.SubjectId = id; r.Card.SubjectName = name; r.RefreshSubject(); }
            FlushCardEdits();
            Db.UpdateCardTexts(sel.Select(r => r.Card));
            ApplySubjectStart(sel.Select(r => r.Card));
        });
    }

    /// <summary>Cards moved into a subject are counted from that subject's start date (the date lives on the subject only).</summary>
    void ApplySubjectStart(IEnumerable<Card> cards)
    {
        var now = DateTime.Now;
        var changed = new List<Card>();
        foreach (var grp in cards.Where(c => c.SubjectId != 0).GroupBy(c => c.CourseId))
        {
            var subs = Db.GetSubjects(grp.Key);
            var course = _cardCourses.FirstOrDefault(c => c.Id == grp.Key);
            foreach (var k in grp)
            {
                var sj = subs.FirstOrDefault(x => x.Id == k.SubjectId);
                if (sj == null || k.StartDate.Date == sj.StartDate.Date) continue;
                ReviewEngine.ChangeStartDate(k, sj.StartDate.Date, ExamById(course?.ExamId), now);
                changed.Add(k);
            }
        }
        if (changed.Count > 0) Db.SaveCardsState(changed);
    }
}
