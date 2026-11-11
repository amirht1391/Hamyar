using System.Globalization;

namespace Hamyar;

/// <summary>The one review method: Smart (it works out its own pacing). Fixed and Manual were removed; the enum and the
/// Method column stay only so existing databases keep loading - every row is migrated to Smart on start-up.</summary>
public enum ReviewMethod
{
    Smart = 2
}

/// <summary>
/// One row of reviewable state shared by Topic and Card, so the engine can work on either
/// without caring which table it came from.
/// </summary>
public interface IReviewable
{
    ReviewMethod Method { get; set; }
    DateTime StartDate { get; set; }           // the day the item was really studied / first scheduled from
    DateTime NextReview { get; set; }
    DateTime? LastReview { get; set; }
    int Step { get; set; }                     // how many reviews completed
    double EaseFactor { get; set; }            // Smart: ease, default 2.5
    double IntervalDays { get; set; }          // Smart: current interval in days (fractional - can be sub-day)
    int Reps { get; set; }                     // Smart: consecutive correct reps
    int Lapses { get; set; }                   // number of "forgot" answers ever
    string CustomIntervalsCsv { get; set; }    // unused since Fixed/Manual were removed (kept for the old database column)
    ExamDifficulty TopicDifficulty { get; }    // how hard the topic / subject is (harder = shorter gaps)
    string ReviewLog { get; set; }             // comma separated yyyy-MM-dd HH:mm of completed reviews (history / timeline display)
}

/// <summary>
/// Quality of a single answer. Today's UI has two buttons (knew it / forgot), which map to Good / Again;
/// Hard and Easy exist for a possible future finer-grained UI and are accepted by Smart already.
/// </summary>
public enum Grade { Again = 0, Hard = 1, Good = 2, Easy = 3 }

/// <summary>
/// Spaced-review engine, usable from both the Study tab and the Flashcards tab. Smart is the only method: it adapts
/// to answer quality, answer speed (flashcards), review history, mastery, and an exam's date/difficulty,
/// including reacting to a late answer by shortening what comes next (see <see cref="Answer"/>).
/// </summary>
public static class ReviewEngine
{
    /// <summary>Display (Shamsi) - for what the user sees.</summary>
    public static string Iso(DateTime d) => Jal.Date(d);
    public static string IsoDateTime(DateTime d) => Jal.Stamp(d);
    /// <summary>Storage (Gregorian) - for the review log and comparisons with stored text.</summary>
    public static string IsoG(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string IsoGDateTime(DateTime d) => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static bool IsSmart(ReviewMethod m) => true;

    /// <summary>(Re)compute NextReview from StartDate and Step, used when nothing has been answered yet or after a restart.</summary>
    public static void Recompute(IReviewable r)
    {
        // first interval is 1 day until a real answer comes in
        r.NextReview = r.StartDate.Date.AddDays(1);
    }

    /// <summary>Resets an item back to its very first review, from its StartDate.</summary>
    public static void Restart(IReviewable r)
    {
        r.Step = 0;
        r.LastReview = null;
        r.ReviewLog = "";
        r.EaseFactor = 2.5;
        r.IntervalDays = 1;
        r.Reps = 0;
        r.Lapses = 0;
        Recompute(r);
    }

    static void LogReview(IReviewable r, DateTime when)
    {
        var stamp = IsoGDateTime(when);
        r.ReviewLog = string.IsNullOrEmpty(r.ReviewLog) ? stamp : r.ReviewLog + "," + stamp;
    }

    /// <summary>
    /// Records one answer and schedules the next review. `knew` drives the simple 2-button UI; Smart turns
    /// that into a Grade (Good when knew, Again when forgot).
    ///
    /// The user can always log a review, any time - whether or not it was actually due, and whether or not
    /// they are inside their open study window. Smart looks at how late (or early) this answer was relative
    /// to when it was actually due, and - only when that gap is large - lets it shorten the interval it would
    /// otherwise have chosen, on top of everything else it adapts to (grade, answer speed, streak, mastery and
    /// the linked exam - see <see cref="AnswerSmart"/>).
    /// </summary>
    public static void Answer(IReviewable r, bool knew, DateTime now, Exam exam = null)
    {
        Answer(r, knew ? Grade.Good : Grade.Again, now, exam);
    }

    public static void Answer(IReviewable r, Grade grade, DateTime now, Exam exam = null, double? speed = null)
    {
        var dueWas = r.NextReview;     // captured before LogReview/Step change it - this is "how late was it"
        LogReview(r, now);
        r.LastReview = now.Date;

        AnswerSmart(r, grade, now, exam, dueWas, speed);
    }

    /// <summary>
    /// Smart scheduling: an SM-2-style core (ease factor moves with answer quality, interval grows on a
    /// correct answer and collapses on a lapse), adjusted by:
    ///  - mastery signal from the review history (more lapses relative to reps -> shorter, more cautious growth);
    ///  - the linked exam's date and difficulty, compressing the interval - down to whole hours or minutes
    ///    when little time is left - so the schedule keeps reviewing right up to the exam and never past it;
    ///  - how LATE this particular answer was: if the user logged it well past when it was actually due,
    ///    Smart treats that gap as a sign the current pace is too loose and pulls the next interval in,
    ///    rather than growing it as if the answer had landed on time. A late-but-correct answer still grows
    ///    the interval overall, just by less than it would have if answered on time.
    /// </summary>
    static void AnswerSmart(IReviewable r, Grade grade, DateTime now, Exam exam, DateTime dueWas, double? speed = null)
    {
        if (r.EaseFactor <= 0) r.EaseFactor = 2.5;

        if (speed.HasValue)
        {
            ApplyAnkiStyleInterval(r, grade, speed.Value);
        }
        else
        {
            // SM-2's quality is 0..5; map our 4 grades onto it.
            int q = grade switch { Grade.Again => 1, Grade.Hard => 3, Grade.Good => 4, Grade.Easy => 5, _ => 4 };

            double newEase = r.EaseFactor + (0.1 - (5 - q) * (0.08 + (5 - q) * 0.02));
            r.EaseFactor = Math.Max(1.3, newEase);

            if (grade == Grade.Again)
            {
                r.Reps = 0;
                r.Lapses++;
                r.IntervalDays = 10.0 / 1440.0; // 10 minutes
            }
            else
            {
                r.Reps++;
                if (r.Reps == 1) r.IntervalDays = 1;
                else if (r.Reps == 2) r.IntervalDays = 3;
                else
                {
                    double mult = r.EaseFactor;
                    if (grade == Grade.Hard) mult *= 0.75;
                    else if (grade == Grade.Easy) mult *= 1.3;
                    r.IntervalDays = Math.Max(3, r.IntervalDays * mult);
                }
            }
        }

        // Mastery signal from history: a item with many lapses relative to its total reviews is clearly
        // not sticking yet, so growth is damped even on a good answer - Smart stays cautious with it.
        int totalAnswers = r.Reps + r.Lapses;
        if (totalAnswers >= 3 && grade != Grade.Again)
        {
            double lapseRate = (double)r.Lapses / totalAnswers;
            if (lapseRate > 0.2)
            {
                double damp = Math.Max(0.4, 1.0 - lapseRate);  // more lapses -> more damping, floored so it never stalls completely
                r.IntervalDays *= damp;
            }
        }

        // Lateness signal: how overdue was THIS answer when it was finally logged, relative to the gap that
        // was supposed to separate it from the review before it. A small lateness (a few hours on a
        // multi-day schedule) is noise and ignored; a large one means the real-world pace has been looser
        // than the schedule assumed, so the next interval is pulled in rather than grown as normal.
        double scheduledGapDays = Math.Max(0.01, r.IntervalDays); // the gap just computed above, before any pull-in
        double latenessDays = Math.Max(0, (now - dueWas).TotalDays);
        if (dueWas > DateTime.MinValue && latenessDays > Math.Max(0.5, scheduledGapDays * 0.5)
            && !(speed.HasValue && grade == Grade.Again))   // a "forgot" in the four-button review is always its own short step
        {
            // the later it was, relative to its own gap, the more the next interval is pulled in - capped
            // so a very late answer still leaves a sane minimum rather than collapsing to almost nothing.
            double overrun = Math.Min(1.0, latenessDays / Math.Max(1, scheduledGapDays));
            double latePull = Math.Max(0.5, 1.0 - overrun * 0.5);   // 1.0 (no pull) down to 0.5 (half) at max overrun
            r.IntervalDays *= latePull;
        }

        r.Step++;
        r.NextReview = ComputeNext(r, r.IntervalDays, grade, now, exam, speed);
    }

    /// <summary>
    /// Where the next review lands, given a base gap in days, the moment of the answer (`now`) and the linked exam:
    /// difficulty scaling, the exam cap measured in FREE time, then sleep / school hours. Used both when an answer is
    /// recorded and when a plan is recalculated after an exam or a date changed, so both always agree.
    /// </summary>
    static DateTime ComputeNext(IReviewable r, double intervalDays, Grade grade, DateTime now, Exam exam, double? speed)
    {
        bool examActive = exam != null && !exam.IsPast;
        bool compact = Cfg.StudyCompact && !(exam != null && exam.IsPast);
        double diffMul = compact ? DifficultyFactor(r.TopicDifficulty) : 1.0;
        double gap = intervalDays;
        if (grade != Grade.Again) gap *= diffMul;

        DateTime? examCapped = null;
        if (examActive && compact && grade != Grade.Again)
        {
            var deadline = exam.When.AddMinutes(-10);
            double freeLeft = StudyWindow.OpenMinutes(now, deadline);
            double share = ExamShare(exam.Difficulty, grade) * diffMul * (speed ?? 1.0);
            double capMin = Math.Max(10, freeLeft * share);
            if (freeLeft >= 10)
            {
                var cp = StudyWindow.AddOpenMinutes(now, capMin, deadline);
                if (!StudyWindow.IsOpen(cp)) cp = StudyWindow.PrevOpen(cp);
                examCapped = cp > now ? cp : now.AddMinutes(10);
            }
        }
        gap = Math.Max(gap, grade == Grade.Again ? 1.0 / 1440 : 10.0 / 1440);   // never less than 10 min (Forgot: its own 5 min step)

        var next = now.AddDays(gap);

        // A short step (under 30 min, e.g. "Forgot" = 5 min) is part of the session happening right now: it stays put.
        if (gap >= 30.0 / 1440 || examCapped != null)
        {
            var opened = StudyWindow.NextOpen(next);
            if (examCapped != null && (opened > examCapped.Value || next > examCapped.Value)) opened = examCapped.Value;
            if (examActive)
            {
                var latest = exam.When.AddMinutes(-10);
                if (opened > latest)
                {
                    var before = StudyWindow.PrevOpen(next < latest ? next : latest);
                    if (before > now.AddMinutes(5)) opened = before;
                }
            }
            next = opened;
        }
        return next;
    }

    // ----- smart recalculation: an exam was added / changed / removed, or a start date moved ------------------------

    /// <summary>
    /// Recomputes WHEN the next review is, keeping everything the item has learned (ease, interval, history, step).
    /// It replays the placement of the last answer with the CURRENT exam, so a closer exam pulls the next review in,
    /// a later or removed exam lets it breathe, and an item that was never answered is simply re-planned from its
    /// start date. A short "forgot" step (under 30 min) is left alone. Returns true when the date changed.
    /// </summary>
    public static bool Reschedule(IReviewable r, Exam exam, DateTime now)
    {
        bool neverAnswered = r.Step == 0 && string.IsNullOrEmpty(r.ReviewLog);
        double gap = neverAnswered ? 1.0 : r.IntervalDays;
        if (!neverAnswered && gap < 30.0 / 1440) return false;

        DateTime anchor;
        if (neverAnswered) anchor = r.StartDate.Date;
        else
        {
            var log = Study.ParseReviewLog(r.ReviewLog);
            anchor = log.Count > 0 ? log[^1] : (r.LastReview ?? r.StartDate.Date);
        }

        var next = ComputeNext(r, gap, Grade.Good, anchor, exam, null);
        if (next == r.NextReview) return false;
        r.NextReview = next;
        return true;
    }

    /// <summary>The start date was edited. Never answered: re-plan from the new date. Already reviewed: the whole upcoming
    /// schedule moves by the same number of days (and is kept before an active exam).</summary>
    public static void ChangeStartDate(IReviewable r, DateTime newDate, Exam exam, DateTime now)
    {
        var old = r.StartDate.Date;
        r.StartDate = newDate.Date;
        bool neverAnswered = r.Step == 0 && string.IsNullOrEmpty(r.ReviewLog);
        if (neverAnswered) { Reschedule(r, exam, now); return; }

        var next = r.NextReview + (newDate.Date - old);
        if (exam != null && !exam.IsPast)
        {
            var latest = exam.When.AddMinutes(-10);
            if (next > latest) next = StudyWindow.PrevOpen(latest);
        }
        r.NextReview = next;
    }

    /// <summary>Gap multiplier for the difficulty of a topic / subject: easy stretches, hard shrinks.</summary>
    public static double DifficultyFactor(ExamDifficulty d) => d switch
    {
        ExamDifficulty.Easy => 1.25,
        ExamDifficulty.Medium => 1.0,
        ExamDifficulty.Hard => 0.8,
        ExamDifficulty.VeryHard => 0.65,
        _ => 1.0
    };

    /// <summary>Share of the free study time left before the exam that one gap may use. A harder exam means a smaller share
    /// (so more reviews); the Easy button gets the biggest share, Medium x0.75, Hard x0.5.</summary>
    static double ExamShare(ExamDifficulty difficulty, Grade grade)
    {
        double share = difficulty switch
        {
            ExamDifficulty.Easy => 0.30,
            ExamDifficulty.Medium => 0.22,
            ExamDifficulty.Hard => 0.17,
            ExamDifficulty.VeryHard => 0.13,
            _ => 0.22
        };
        return share * (grade switch { Grade.Hard => 0.5, Grade.Good => 0.75, _ => 1.0 });
    }

    // ----- Anki-style four-button answers (Easy / Medium / Hard / Forgot) used by the flashcard review -----
    //
    // The base interval depends on the button; it is then scaled by how fast the answer came (speed factor 1.0 .. 0.25):
    //   first successful review :  Hard = 6 h   Medium = 12 h   Easy = 1 day        (each x speed)
    //   later reviews           :  Hard = prev x 1.2   Medium = prev x ease   Easy = prev x ease x 1.3   (each x speed)
    //   Forgot                  :  5 minutes, back to the learning stage, ease -0.20
    // Ease moves like Anki's: Hard -0.15, Medium 0, Easy +0.15 (never below 1.3). Hard < Medium < Easy always holds.
    // Then, on top: lapse damping, lateness, the EXAM cap (a fraction of the time left, by button and exam difficulty),
    // and finally sleep / school hours. The text on each of the four buttons is this exact result.
    static void ApplyAnkiStyleInterval(IReviewable r, Grade grade, double speed)
    {
        double ease = r.EaseFactor <= 0 ? 2.5 : r.EaseFactor;
        if (grade == Grade.Again)
        {
            r.Reps = 0;
            r.Lapses++;
            r.IntervalDays = 5.0 / 1440.0;
            r.EaseFactor = Math.Max(1.3, ease - 0.20);
            return;
        }

        double prev = r.IntervalDays;
        bool first = r.Reps == 0;
        r.Reps++;
        if (first)
        {
            double baseDays = grade switch { Grade.Hard => 0.25, Grade.Good => 0.5, _ => 1.0 };
            r.IntervalDays = baseDays * speed;
        }
        else
        {
            double mult = grade switch
            {
                Grade.Hard => Math.Max(1.05, 1.2 * speed),
                Grade.Good => Math.Max(1.15, ease * speed),
                _ => Math.Max(1.3, ease * 1.3 * speed)
            };
            r.IntervalDays = prev * mult;
        }
        r.EaseFactor = Math.Max(1.3, ease + (grade == Grade.Hard ? -0.15 : grade == Grade.Easy ? 0.15 : 0.0));
    }

    /// <summary>Seconds a flashcard may stay unanswered; after that the answer is shown and the card counts as "forgot".</summary>
    public const int AnswerTimeLimitSeconds = 30;

    /// <summary>Scales the next interval by how long the user thought before pressing "Show answer":
    /// 1.0 up to 3 s (known at once), falling in a straight line to 0.5 at 15 s (Easy 1 day becomes 12 h),
    /// then to 0.25 at 30 s. The slower the answer, the shorter the gap - the card comes back sooner.</summary>
    public static double SpeedFactor(double seconds)
    {
        double t = Math.Clamp(seconds, 0.0, AnswerTimeLimitSeconds);
        if (t <= 3.0) return 1.0;
        if (t <= 15.0) return 1.0 - 0.5 * (t - 3.0) / 12.0;
        return 0.5 - 0.25 * (t - 15.0) / 15.0;
    }

    /// <summary>"5 min", "12 h", "3 days", "1.5 months" - the interval written on each answer button.</summary>
    public static string FormatInterval(TimeSpan ts)
    {
        double min = Math.Max(1, ts.TotalMinutes);
        if (min < 60) return string.Format(Loc.Get("iv_min"), (int)Math.Round(min));
        double h = min / 60.0;
        if (Math.Round(h) < 24) return string.Format(Loc.Get("iv_hr"), (int)Math.Round(h));
        double d = h / 24.0;
        if (d < 30) return string.Format(Loc.Get("iv_day"), Math.Max(1, (int)Math.Round(d)));
        if (d < 365) return string.Format(Loc.Get("iv_month"), (d / 30.4).ToString("0.#", CultureInfo.InvariantCulture));
        return string.Format(Loc.Get("iv_year"), (d / 365.0).ToString("0.#", CultureInfo.InvariantCulture));
    }

    public static bool IsDue(IReviewable r, DateTime now) => r.NextReview <= now;

    /// <summary>Human due label: "overdue by N", "in N minutes/hours", or "in N days".</summary>
    public static string DueLabel(IReviewable r, DateTime now)
    {
        if (r.NextReview.Date == now.Date && r.NextReview > now)
        {
            var mins = (int)Math.Round((r.NextReview - now).TotalMinutes);
            if (mins <= 0) return Loc.Get("study_today");
            if (mins < 60) return string.Format(Loc.Get("study_due_in_min"), mins);
            return string.Format(Loc.Get("study_due_in_hr"), Math.Round(mins / 60.0, 1));
        }
        int d = (r.NextReview.Date - now.Date).Days;
        if (d < 0) return string.Format(Loc.Get("study_overdue"), -d);
        if (d == 0) return Loc.Get("study_today");
        return string.Format(Loc.Get("study_next"), Iso(r.NextReview), d);
    }
}
