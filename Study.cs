using System.Globalization;

namespace Hamyar;

public enum TopicKind { Class = 0, SelfStudy = 1 }

public class Course
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Flashcard courses (Scope=1) use ONE review method for every card in the course - the user does
    /// not pick a method per card. Study courses (Scope=0) don't use this; each Topic has its own method.</summary>
    public ReviewMethod Method { get; set; } = ReviewMethod.Smart;
    public string CustomIntervalsCsv { get; set; } = "";
    /// <summary>The exam this flashcard course is being prepared for, if any (Scope=1 only).</summary>
    public long? ExamId { get; set; }
    public string ExamTitle { get; set; } = "";
    /// <summary>Flashcard courses: the part AFTER the fixed word "All" in the default subject chip
    /// (e.g. "lessons" -> "All lessons"). Empty = use the default word.</summary>
    public string SubjectAllName { get; set; } = "";
    public string AllSubjectsLabel =>
        Loc.Get("subj_all_prefix") + " " + (string.IsNullOrWhiteSpace(SubjectAllName) ? Loc.Get("subj_all_default") : SubjectAllName);
}

/// <summary>A subject (lesson / chapter) inside a flashcard course.</summary>
public class Subject
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public string Name { get; set; } = "";
    public int CardCount { get; set; }
    public ExamDifficulty Difficulty { get; set; } = ExamDifficulty.Medium;
    /// <summary>Start date of the whole subject: every card in it is counted from this day (set in the subject settings).</summary>
    public DateTime StartDate { get; set; } = DateTime.Today;
}

/// <summary>Difficulty the user perceives for an exam: used by Smart to decide how much extra pressure to
/// put on review spacing as the date approaches.</summary>
public enum ExamDifficulty { Easy = 0, Medium = 1, Hard = 2, VeryHard = 3 }

/// <summary>An exam: a date/time to prepare for. Study topics and flashcard courses can each link to one;
/// Smart uses that link to pace reviews so mastery peaks right before it.</summary>
public class Exam
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime When { get; set; } = DateTime.Today.AddDays(7);   // date + time of the exam
    public ExamDifficulty Difficulty { get; set; } = ExamDifficulty.Medium;
    public string Notes { get; set; } = "";

    public int TopicCount { get; set; }     // how many Study topics link to this exam (for display)
    public int CardCourseCount { get; set; } // how many Flashcard courses link to this exam (for display)

    public bool IsPast => When < DateTime.Now;
    public int DaysLeft => (When.Date - DateTime.Today).Days;
    public string DateText => Jal.Stamp(When);
    public string DifficultyText => Loc.Get("exam_diff_" + (int)Difficulty);
    public string CountsText => string.Format(Loc.Get("exam_counts"), TopicCount, CardCourseCount);
    public string WhenText => IsPast ? Loc.Get("exam_past") :
        DaysLeft == 0 ? Loc.Get("exam_today") :
        string.Format(Loc.Get("exam_in_days"), DaysLeft);
}

/// <summary>One pill of the review timeline shown on a topic/card: the actual date+time.</summary>
public class PlanStep
{
    public string DateText { get; set; } = "";     // 2026-10-14 18:30
    public string Mark { get; set; } = "";         // ✓ done, ● next, ○ later
    public int State { get; set; }                 // 0 done, 1 next, 2 later
}

/// <summary>One thing to remember: taught in class, or studied by yourself. It is reviewed on a spaced schedule.</summary>
public class Topic : IReviewable
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public string CourseName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public TopicKind Kind { get; set; }
    public ReviewMethod Method { get; set; } = ReviewMethod.Smart;
    public DateTime StudiedOn { get; set; } = DateTime.Today;     // the START date: the day you really studied it
    public int Step { get; set; }                                  // number of reviews already done
    public DateTime? LastReview { get; set; }
    public DateTime NextReview { get; set; } = DateTime.Today.AddDays(1);
    public string ReviewLog { get; set; } = "";                    // date+times of the reviews done, comma separated
    public double EaseFactor { get; set; } = 2.5;
    public double IntervalDays { get; set; } = 1;
    public int Reps { get; set; }
    public int Lapses { get; set; }
    public string CustomIntervalsCsv { get; set; } = "";
    /// <summary>1..5: how well the user currently knows this topic. Shown for the user's own reference; Smart's
    /// own mastery signal is computed from the actual review history (lapses vs. reps), not this field.</summary>
    public int Mastery { get; set; } = 3;
    /// <summary>How hard this topic is for the user (same four levels as an exam). Harder = shorter gaps.</summary>
    public ExamDifficulty Difficulty { get; set; } = ExamDifficulty.Medium;
    public ExamDifficulty TopicDifficulty => Difficulty;
    /// <summary>The exam this topic is being prepared for, if any.</summary>
    public long? ExamId { get; set; }
    public string ExamTitle { get; set; } = "";
    /// <summary>The full linked exam (date, difficulty), populated by Db.GetTopics - used to project the
    /// plan and, for Smart, to pace reviews. Null when ExamId is null.</summary>
    public Exam Exam { get; set; }

    DateTime IReviewable.StartDate { get => StudiedOn; set => StudiedOn = value; }

    public bool IsDue => ReviewEngine.IsDue(this, DateTime.Now);
    /// <summary>Due AND not inside the sleep/school window - what "to review" should actively push, as
    /// opposed to merely list. The "to review" list itself still shows everything due regardless of the
    /// window (see IsDue): this is only for passive nudges (badges, the wake-up reminder).</summary>
    public bool IsSuggestedNow => IsDue && StudyWindow.IsOpen(DateTime.Now);
    public int DaysUntil => (NextReview.Date - DateTime.Today).Days;

    public string Subtitle =>
        CourseName + "  ·  " + Loc.Get(Kind == TopicKind.Class ? "kind_class" : "kind_self") +
        "  ·  " + Loc.Get("exam_diff_" + (int)Difficulty) + "  ·  " + Loc.Get("study_learned") + " " + ReviewEngine.Iso(StudiedOn) +
        (ExamId.HasValue ? "  ·  " + "\uD83C\uDFAF " + ExamTitle : "");

    public string StepText => string.Format(Loc.Get("study_done_count"), Step);
    public string DueText => ReviewEngine.DueLabel(this, DateTime.Now);

    public List<DateTime> ReviewDates => Study.ParseReviewLog(ReviewLog);

}

/// <summary>Shared review-log parsing used by both Topic and Card.</summary>
static class Study
{
    public static List<DateTime> ParseReviewLog(string log)
    {
        var list = new List<DateTime>();
        foreach (var p in (log ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var s = p.Trim();
            if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                list.Add(dt);
            else if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                list.Add(d); // older log entries saved as date-only
        }
        return list;
    }
}

/// <summary>A flashcard, reviewed on one of the 3 review methods (the course's method - see Course.Method).</summary>
public class Card : IReviewable
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public string CourseName { get; set; } = "";
    /// <summary>0 = the card is not in any subject (it only shows under the course's "All ..." chip).</summary>
    public long SubjectId { get; set; }
    /// <summary>Manual order in the quick-edit list (drag to change).</summary>
    public long Pos { get; set; }
    public string SubjectName { get; set; } = "";
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public int Box { get; set; } = 1;                   // kept for display/back-compat; derived from Step for table methods
    /// <summary>Set from the owning Course's method whenever the card is loaded/saved - the user picks the
    /// method once per course, not per card. Kept as a real column anyway so the engine still works uniformly.</summary>
    public ReviewMethod Method { get; set; } = ReviewMethod.Smart;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime NextReview { get; set; } = DateTime.Today.AddDays(1);
    public int Reviews { get; set; }
    public int Lapses { get; set; }
    public int Step { get; set; }
    public DateTime? LastReview { get; set; }
    public string ReviewLog { get; set; } = "";
    public double EaseFactor { get; set; } = 2.5;
    public double IntervalDays { get; set; } = 1;
    public int Reps { get; set; }
    public string CustomIntervalsCsv { get; set; } = "";
    /// <summary>The exam this card's course is being prepared for, if any (read from the course, shown for convenience).</summary>
    public long? ExamId { get; set; }
    public string ExamTitle { get; set; } = "";
    /// <summary>The full linked exam (date, difficulty), populated by Db.GetCards from the course's own link.</summary>
    public Exam Exam { get; set; }
    /// <summary>Difficulty of the card's subject (Medium when it has none).</summary>
    public ExamDifficulty TopicDifficulty { get; set; } = ExamDifficulty.Medium;

    /// <summary>A throw-away copy used to preview what each answer button would schedule.</summary>
    public Card CloneForPreview() => (Card)MemberwiseClone();

    public bool IsDue => ReviewEngine.IsDue(this, DateTime.Now);
    public bool IsSuggestedNow => IsDue && StudyWindow.IsOpen(DateTime.Now);
    /// <summary>The badge on a flashcard: "New" until it has been answered, then its current interval.</summary>
    public string BoxText => Reviews == 0 ? Loc.Get("card_state_new") : ReviewEngine.FormatInterval(TimeSpan.FromDays(Math.Max(IntervalDays, 1.0 / 1440)));
    public string DueText => ReviewEngine.DueLabel(this, DateTime.Now);
    public string Subtitle =>
        CourseName + (string.IsNullOrEmpty(SubjectName) ? "" : " › " + SubjectName) + "  ·  " + Loc.Get("card_started") + " " + ReviewEngine.Iso(StartDate) +
        "  ·  " + string.Format(Loc.Get("card_stats"), Reviews, Lapses) +
        (ExamId.HasValue ? "  ·  " + "\uD83C\uDFAF " + ExamTitle : "");

    public List<DateTime> ReviewDates => Study.ParseReviewLog(ReviewLog);

    /// <summary>The ONE line shown under a flashcard: the exact date+time of its next review and how long is left
    /// (compact units: m, h, d, mo, y). Only the next review - never the whole future schedule.</summary>
    public string NextText
    {
        get
        {
            var now = DateTime.Now;
            var when = ReviewEngine.IsoDateTime(NextReview);
            if (NextReview > now)
                return string.Format(Loc.Get("card_next_in"), when, ReviewEngine.FormatInterval(NextReview - now));
            var late = now - NextReview;
            return late.TotalMinutes < 1
                ? string.Format(Loc.Get("card_next_now"), when)
                : string.Format(Loc.Get("card_next_overdue"), when, ReviewEngine.FormatInterval(late));
        }
    }
}

/// <summary>
/// Thin facade over ReviewEngine for Cards, kept so call sites read naturally ("the user knew it" /
/// "forgot it") without exposing the Grade enum everywhere.
/// </summary>
public static class Leitner
{
    public static void Restart(Card c)
    {
        ReviewEngine.Restart(c);
        c.Box = 1;
    }

    public static void Know(Card c, DateTime today, Exam exam = null)
    {
        c.Reviews++;
        ReviewEngine.Answer(c, true, today, exam);
    }

    /// <summary>Four-button answer (Easy / Medium / Hard / Forgot). `speed` (see ReviewEngine.SpeedFactor) only matters for Smart cards.</summary>
    public static void Rate(Card c, Grade grade, DateTime now, Exam exam = null, double? speed = null)
    {
        c.Reviews++;
        ReviewEngine.Answer(c, grade, now, exam, speed);
        c.Box = grade == Grade.Again ? 1 : Math.Min(c.Box + 1, 5);
    }

    public static void Forgot(Card c, DateTime today, Exam exam = null)
    {
        c.Reviews++;
        ReviewEngine.Answer(c, false, today, exam);
        c.Box = 1;
    }
}
