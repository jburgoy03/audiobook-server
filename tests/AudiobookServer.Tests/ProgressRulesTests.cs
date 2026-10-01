using AudiobookServer.Core.Progress;

namespace AudiobookServer.Tests;

public class ProgressRulesTests
{
    private static readonly Guid Laptop = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Phone = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static ProgressPoint At(double position, int minutesAfterT0, Guid? device, bool finished = false) =>
        new(position, T0.AddMinutes(minutesAfterT0), device, finished);

    [Fact]
    public void The_first_report_is_accepted()
    {
        var d = ProgressRules.Decide(null, At(10, 0, Laptop), isOverride: false);
        Assert.Equal(new ProgressDecision(true, ProgressReason.First), d);
    }

    [Fact]
    public void Furthest_wins_across_devices()
    {
        var stored = At(1000, 0, Laptop);

        Assert.Equal(ProgressReason.Further, ProgressRules.Decide(stored, At(2000, 5, Phone), false).Reason);
        Assert.True(ProgressRules.Decide(stored, At(2000, 5, Phone), false).Accepted);
    }

    [Fact]
    public void An_older_report_that_is_further_ahead_still_wins()
    {
        // An offline phone uploading hours later. Known gap: also accepted when the
        // newer, lower position was a deliberate rewind elsewhere (see ProgressRules).
        var stored = At(1000, 60, Laptop);
        var d = ProgressRules.Decide(stored, At(5000, 0, Phone), false);
        Assert.Equal(new ProgressDecision(true, ProgressReason.Further), d);
    }

    [Fact]
    public void Another_device_behind_is_rejected_even_when_newer()
    {
        var stored = At(5000, 0, Phone);
        var d = ProgressRules.Decide(stored, At(1000, 60, Laptop), false);
        Assert.Equal(new ProgressDecision(false, ProgressReason.Behind), d);
    }

    [Fact]
    public void The_same_device_can_rewind()
    {
        var stored = At(5000, 0, Laptop);
        var d = ProgressRules.Decide(stored, At(1000, 1, Laptop), false);
        Assert.Equal(new ProgressDecision(true, ProgressReason.SameDevice), d);
    }

    [Fact]
    public void A_stale_report_from_the_same_device_falls_back_to_furthest_wins()
    {
        // Older than what this device already stored, and behind it: a retried upload.
        var stored = At(5000, 10, Laptop);
        var d = ProgressRules.Decide(stored, At(1000, 0, Laptop), false);
        Assert.Equal(new ProgressDecision(false, ProgressReason.Behind), d);
    }

    [Fact]
    public void A_report_without_a_device_never_counts_as_the_same_device()
    {
        var stored = At(5000, 0, null);
        var d = ProgressRules.Decide(stored, At(1000, 1, null), false);
        Assert.False(d.Accepted);
    }

    [Fact]
    public void Override_wins_regardless()
    {
        var stored = At(5000, 60, Phone, finished: true);
        var d = ProgressRules.Decide(stored, At(10, 0, Laptop), isOverride: true);
        Assert.Equal(new ProgressDecision(true, ProgressReason.Override), d);
    }

    [Fact]
    public void Finishing_outranks_any_position()
    {
        var stored = At(5000, 0, Phone);
        var d = ProgressRules.Decide(stored, At(100, 1, Laptop, finished: true), false);
        Assert.Equal(new ProgressDecision(true, ProgressReason.Finished), d);
    }

    [Fact]
    public void A_finished_book_rejects_positions_from_other_devices()
    {
        var stored = At(100, 0, Phone, finished: true);
        var d = ProgressRules.Decide(stored, At(99_999, 5, Laptop), false);
        Assert.Equal(new ProgressDecision(false, ProgressReason.BehindFinished), d);
    }

    [Fact]
    public void The_device_that_finished_can_start_again()
    {
        var stored = At(36_000, 0, Laptop, finished: true);
        var d = ProgressRules.Decide(stored, At(30, 5, Laptop), false);
        Assert.Equal(new ProgressDecision(true, ProgressReason.SameDevice), d);
    }

    [Fact]
    public void A_tie_is_accepted_so_the_latest_device_is_recorded()
    {
        var stored = At(1000, 0, Phone);
        Assert.True(ProgressRules.Decide(stored, At(1000, 5, Laptop), false).Accepted);
    }
}
