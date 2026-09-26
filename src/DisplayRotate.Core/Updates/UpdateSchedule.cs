namespace DisplayRotate.Core.Updates;

public static class UpdateSchedule
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    /// <summary>
    /// Whether the daily check is due. Never checked is due. A stamp in the future is due too:
    /// a clock that went backwards would otherwise silence the check until it caught up, which
    /// could be years.
    /// </summary>
    public static bool IsCheckDue(DateTimeOffset? last, DateTimeOffset now) =>
        last is null || last > now || now - last >= CheckInterval;
}
