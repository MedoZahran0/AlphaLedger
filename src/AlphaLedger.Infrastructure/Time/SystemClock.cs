using AlphaLedger.Core.Abstractions;

namespace AlphaLedger.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly TodayLocal => DateOnly.FromDateTime(DateTime.Now);
}
