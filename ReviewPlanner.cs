namespace Hamyar;

/// <summary>A standalone, mutable IReviewable used only for simulating a plan - never persisted. A clone of
/// a real Topic/Card's state, advanced step by step through ReviewEngine.Answer to project forward.</summary>
class SimReviewable : IReviewable
{
    public ReviewMethod Method { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime NextReview { get; set; }
    public DateTime? LastReview { get; set; }
    public int Step { get; set; }
    public double EaseFactor { get; set; }
    public double IntervalDays { get; set; }
    public int Reps { get; set; }
    public int Lapses { get; set; }
    public string CustomIntervalsCsv { get; set; } = "";
    public string ReviewLog { get; set; } = "";
    public ExamDifficulty TopicDifficulty { get; set; }

    public static SimReviewable From(IReviewable r) => new()
    {
        Method = r.Method,
        StartDate = r.StartDate,
        NextReview = r.NextReview,
        LastReview = r.LastReview,
        Step = r.Step,
        EaseFactor = r.EaseFactor,
        IntervalDays = r.IntervalDays,
        Reps = r.Reps,
        Lapses = r.Lapses,
        CustomIntervalsCsv = r.CustomIntervalsCsv,
        ReviewLog = r.ReviewLog,
        TopicDifficulty = r.TopicDifficulty
    };
}

/// <summary>
/// Projects an item's FULL remaining review schedule - not just the single next review - by simulating
/// forward through ReviewEngine.Answer on a throwaway clone of its state, so the projection always matches
/// exactly what the real engine would actually schedule (same exam pressure, same window-safety, same
/// per-method rules). Used to show the complete upcoming plan for every method, including Smart, instead
/// of only ever showing one upcoming step.
/// </summary>
public static class ReviewPlanner
{
    /// <summary>
    /// Projects up to `maxSteps` reviews forward, assuming a "Good" answer each time (the realistic planning
    /// assumption - "if you keep reviewing on schedule and remembering it, here's what's coming"). Stops
    /// early once a projected review would land on/after the linked exam, or once maxSteps is reached, or
    /// (safety) after 60 steps regardless of maxSteps, so a degenerate setting can't run away.
    /// </summary>
    public static List<DateTime> ProjectUpcoming(IReviewable r, Exam exam, int maxSteps = 12, double? speed = null)
    {
        var sim = SimReviewable.From(r);
        var result = new List<DateTime>();
        int hardCap = Math.Min(maxSteps, 60);

        for (int i = 0; i < hardCap; i++)
        {
            var before = sim.NextReview;
            if (exam != null && !exam.IsPast && before >= exam.When) break;

            ReviewEngine.Answer(sim, Grade.Good, before, exam, speed);
            result.Add(sim.NextReview);

            // a step that didn't actually move forward (degenerate config) would loop forever otherwise
            if (sim.NextReview <= before) break;
        }
        return result;
    }

    /// <summary>
    /// Builds the full plan timeline for display: every DONE review (from the real log), the next review,
    /// and the complete projected remaining schedule up to the exam (or `maxSteps` reviews if there is no
    /// exam, or if the exam is far enough that projecting further isn't useful). Every entry carries
    /// the real date+time.
    /// </summary>
    public static List<PlanStep> BuildFullPlan(IReviewable r, DateTime startDate, Exam exam, List<DateTime> doneLog, int maxSteps = 24, double? speed = null)
    {
        var list = new List<PlanStep>();
        foreach (var d in doneLog)
            list.Add(new PlanStep { DateText = ReviewEngine.IsoDateTime(d), Mark = "✓", State = 0 });

        list.Add(new PlanStep { DateText = ReviewEngine.IsoDateTime(r.NextReview), Mark = "●", State = 1 });

        var upcoming = ProjectUpcoming(r, exam, maxSteps, speed);
        foreach (var d in upcoming)
            list.Add(new PlanStep { DateText = ReviewEngine.IsoDateTime(d), Mark = "○", State = 2 });

        return list;
    }
}
