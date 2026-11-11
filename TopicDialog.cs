using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

/// <summary>Add / edit one study topic (what was taught in class, or what you studied yourself).</summary>
public class TopicDialog : Window
{
    readonly Topic _t;
    readonly TextBox _title = new(), _notes = new(), _date = new();
    readonly List<RadioButton> _courseChips = new(), _kindChips = new(), _examChips = new(), _diffChips = new();
    readonly CheckBox _restart = new();

    public TopicDialog(Window owner, Topic t, List<Course> courses, long defaultCourseId, List<Exam> exams)
    {
        _t = t;
        UI.Setup(this, owner, true);
        Title = Loc.Get("study_topic");
        Width = 580;
        Height = 760;
        MinHeight = 500;

        var ltr = System.Windows.FlowDirection.LeftToRight;
        var root = new StackPanel { Margin = new Thickness(20) };
        UI.BindFlow(root);

        // ---- title + notes (notes: 4 visible lines)
        _title.Text = t.Title;
        _notes.Text = t.Notes;
        _notes.AcceptsReturn = true;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.Height = 92;
        _notes.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        root.Children.Add(UI.Lbl("item_title"));
        root.Children.Add(_title);
        root.Children.Add(UI.Lbl("notes"));
        root.Children.Add(_notes);

        // ---- course
        root.Children.Add(UI.Lbl("study_course"));
        long selected = t.CourseId != 0 ? t.CourseId : (defaultCourseId != 0 ? defaultCourseId : courses[0].Id);
        var cw = new WrapPanel();
        foreach (var c in courses)
        {
            var rb = new RadioButton
            {
                GroupName = "tcourse",
                Tag = c.Id,
                Content = c.Name,
                Style = UI.S("Chip"),
                IsChecked = c.Id == selected,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(2)
            };
            _courseChips.Add(rb);
            cw.Children.Add(rb);
        }
        root.Children.Add(cw);

        // ---- kind
        root.Children.Add(UI.Lbl("study_kind"));
        var kw = new WrapPanel();
        foreach (var k in new[] { TopicKind.Class, TopicKind.SelfStudy })
        {
            var rb = UI.Chip("tkind", ((int)k).ToString(), k == TopicKind.Class ? "kind_class" : "kind_self", true);
            rb.IsChecked = k == t.Kind;
            _kindChips.Add(rb);
            kw.Children.Add(rb);
        }
        root.Children.Add(kw);

        // ---- difficulty of the topic
        root.Children.Add(UI.Lbl("topic_difficulty"));
        var dw = new WrapPanel();
        foreach (ExamDifficulty d in Enum.GetValues(typeof(ExamDifficulty)))
        {
            var rb = UI.Chip("tdiff", ((int)d).ToString(), "exam_diff_" + (int)d, true);
            rb.IsChecked = d == t.Difficulty;
            _diffChips.Add(rb);
            dw.Children.Add(rb);
        }
        root.Children.Add(dw);

        // ---- date
        root.Children.Add(UI.Lbl("study_date"));
        _date.Text = Jal.Date(t.StudiedOn);
        _date.Width = 150;
        _date.FlowDirection = ltr;
        _date.Margin = new Thickness(0, 3, 8, 3);
        var dr = new StackPanel { Orientation = Orientation.Horizontal };
        dr.Children.Add(_date);
        dr.Children.Add(UI.Btn("today", (s, e) => _date.Text = Jal.Date(DateTime.Today), true));
        dr.Children.Add(UI.Btn("yesterday", (s, e) => _date.Text = Jal.Date(DateTime.Today.AddDays(-1)), true));
        root.Children.Add(dr);

        foreach (var key in new[] { "study_start_note" })
        {
            var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            UI.Bind(hint, TextBlock.TextProperty, key);
            root.Children.Add(hint);
        }

        // ---- exam (Smart uses this to pace reviews toward full mastery before the exam date)
        root.Children.Add(UI.Lbl("exam_label"));
        var ew = new WrapPanel();
        var none = UI.Chip("texam", "0", "exam_none", true);
        none.IsChecked = !t.ExamId.HasValue;
        _examChips.Add(none);
        ew.Children.Add(none);
        foreach (var ex in exams)
        {
            var rb = new RadioButton
            {
                GroupName = "texam",
                Tag = ex.Id.ToString(),
                Content = ex.Title + "  (" + ex.DateText + ")",
                Style = UI.S("Chip"),
                IsChecked = t.ExamId == ex.Id,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(2)
            };
            _examChips.Add(rb);
            ew.Children.Add(rb);
        }
        root.Children.Add(ew);
        var examNote = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0) };
        examNote.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        UI.Bind(examNote, TextBlock.TextProperty, "exam_link_note");
        root.Children.Add(examNote);

        if (t.Id != 0)
        {
            _restart.Margin = new Thickness(0, 14, 0, 0);
            UI.Bind(_restart, ContentControl.ContentProperty, "study_restart");
            root.Children.Add(_restart);
        }

        // ---- save / cancel
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

    void Save()
    {
        if (!Jal.TryParse(_date.Text, out var d))
        {
            Dialogs.Info(this, Loc.Get("invalid_dt"));
            return;
        }

        _t.Title = string.IsNullOrWhiteSpace(_title.Text) ? Loc.Get("untitled") : _title.Text.Trim();
        _t.Notes = _notes.Text;
        _t.CourseId = (long)_courseChips.First(c => c.IsChecked == true).Tag;
        _t.Kind = (TopicKind)int.Parse((string)_kindChips.First(c => c.IsChecked == true).Tag);

        var oldDifficulty = _t.Difficulty;
        var oldExamId = _t.ExamId;
        _t.Difficulty = (ExamDifficulty)int.Parse((string)_diffChips.First(c => c.IsChecked == true).Tag);
        bool dateChanged = _t.StudiedOn.Date != d.Date;
        var examTag = (string)_examChips.First(c => c.IsChecked == true).Tag;
        _t.ExamId = examTag == "0" ? null : long.Parse(examTag);
        var exam = (Owner as MainWindow)?.ExamById(_t.ExamId);
        var now = DateTime.Now;

        // new topic or explicit restart: start over from the (new) date, planned against the exam.
        // Changed start date: the plan is recalculated at once (never reviewed: from the new date; reviewed: shifted by the same days).
        // Changed exam / difficulty: the next review is recalculated against it.
        if (_t.Id == 0 || _restart.IsChecked == true)
        {
            _t.StudiedOn = d.Date;
            ReviewEngine.Restart(_t);
            ReviewEngine.Reschedule(_t, exam, now);
        }
        else
        {
            if (dateChanged) ReviewEngine.ChangeStartDate(_t, d.Date, exam, now);
            if (_t.ExamId != oldExamId || _t.Difficulty != oldDifficulty) ReviewEngine.Reschedule(_t, exam, now);
        }

        Db.SaveTopic(_t);
        DialogResult = true;
    }
}
