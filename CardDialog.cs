using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

/// <summary>Add / edit one flashcard (question, answer, course, subject). The start date comes from the subject.</summary>
public class CardDialog : Window
{
    readonly Card _c;
    readonly TextBox _front = new(), _back = new();
    readonly List<RadioButton> _courseChips = new();
    readonly CheckBox _restart = new();
    readonly List<Course> _courses;
    readonly WrapPanel _subjectWrap = new();
    readonly List<RadioButton> _subjectChips = new();

    public CardDialog(Window owner, Card card, List<Course> courses, long defaultCourseId, long defaultSubjectId = 0)
    {
        _c = card;
        _courses = courses;
        UI.Setup(this, owner, true);
        Title = Loc.Get("card_dialog");
        Width = 580;
        Height = 740;
        MinHeight = 520;

        var root = new StackPanel { Margin = new Thickness(20) };
        UI.BindFlow(root);

        foreach (var box in new[] { _front, _back })
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.Height = 92;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        _front.Text = card.Front;
        _back.Text = card.Back;
        root.Children.Add(UI.Lbl("card_front"));
        root.Children.Add(_front);
        root.Children.Add(UI.Lbl("card_back"));
        root.Children.Add(_back);

        // course
        root.Children.Add(UI.Lbl("study_course"));
        long selected = card.CourseId != 0 ? card.CourseId : (defaultCourseId != 0 ? defaultCourseId : courses[0].Id);
        var cw = new WrapPanel();
        foreach (var co in courses)
        {
            var rb = new RadioButton
            {
                GroupName = "ccourse",
                Tag = co.Id,
                Content = co.Name,
                Style = UI.S("Chip"),
                IsChecked = co.Id == selected,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(2)
            };
            rb.Checked += (s, e) => RebuildSubjects(0);       // subjects belong to one course: reload them for the new one
            _courseChips.Add(rb);
            cw.Children.Add(rb);
        }
        root.Children.Add(cw);

        // subject (lesson / chapter) inside the chosen course
        root.Children.Add(UI.Lbl("subj_label"));
        root.Children.Add(_subjectWrap);
        RebuildSubjects(card.Id != 0 ? card.SubjectId : defaultSubjectId);

        if (card.Id != 0)
        {
            _restart.Margin = new Thickness(0, 14, 0, 0);
            UI.Bind(_restart, ContentControl.ContentProperty, "card_restart");
            root.Children.Add(_restart);
        }

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        footer.Children.Add(UI.Btn("save", (s, e) => Save()));
        footer.Children.Add(UI.Btn("cancel", (s, e) => DialogResult = false, true));
        root.Children.Add(footer);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }

    /// <summary>Fills the subject chips for the course that is currently ticked. The first chip means "no subject".</summary>
    void RebuildSubjects(long selectedId)
    {
        _subjectWrap.Children.Clear();
        _subjectChips.Clear();
        var courseChip = _courseChips.FirstOrDefault(c => c.IsChecked == true);
        if (courseChip == null) return;
        var subs = Db.GetSubjects((long)courseChip.Tag);
        bool found = selectedId != 0 && subs.Any(x => x.Id == selectedId);

        var none = new RadioButton { GroupName = "csubject", Tag = 0L, Style = UI.S("Chip"), IsChecked = !found,
            Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2) };
        UI.Bind(none, ContentControl.ContentProperty, "subj_none");
        _subjectChips.Add(none);
        _subjectWrap.Children.Add(none);
        foreach (var sj in subs)
        {
            var rb = new RadioButton { GroupName = "csubject", Tag = sj.Id, Content = sj.Name, Style = UI.S("Chip"),
                IsChecked = found && sj.Id == selectedId, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2) };
            _subjectChips.Add(rb);
            _subjectWrap.Children.Add(rb);
        }
    }

    void Save()
    {
        if (string.IsNullOrWhiteSpace(_front.Text) && string.IsNullOrWhiteSpace(_back.Text))
        {
            Dialogs.Info(this, Loc.Get("untitled"));
            return;
        }

        _c.Front = _front.Text.Trim();
        _c.Back = _back.Text.Trim();
        var newCourseId = (long)_courseChips.First(c => c.IsChecked == true).Tag;
        bool courseChanged = _c.Id != 0 && _c.CourseId != newCourseId;
        _c.CourseId = newCourseId;
        _c.SubjectId = (long)(_subjectChips.FirstOrDefault(c => c.IsChecked == true)?.Tag ?? 0L);

        // the start date belongs to the subject: a card in a subject is counted from the subject's day (no subject: kept / today)
        var subj = Db.GetSubjects(newCourseId).FirstOrDefault(x => x.Id == _c.SubjectId);
        var d = subj != null ? subj.StartDate.Date : (_c.Id == 0 ? DateTime.Today : _c.StartDate.Date);
        bool dateChanged = _c.StartDate.Date != d.Date;
        var course = _courses.FirstOrDefault(x => x.Id == newCourseId);
        var exam = (Owner as MainWindow)?.ExamById(course?.ExamId);
        var now = DateTime.Now;

        // a new card, an explicit restart, or moving to another course: start over, planned against that course's exam.
        // Only the date changed: the plan is recalculated at once (never reviewed: from the new date; reviewed: shifted by the same days).
        if (_c.Id == 0 || _restart.IsChecked == true || courseChanged)
        {
            _c.StartDate = d.Date;
            Leitner.Restart(_c);
            ReviewEngine.Reschedule(_c, exam, now);
        }
        else if (dateChanged) ReviewEngine.ChangeStartDate(_c, d.Date, exam, now);

        Db.SaveCard(_c);
        DialogResult = true;
    }
}
