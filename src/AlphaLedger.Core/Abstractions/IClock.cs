namespace AlphaLedger.Core.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly TodayLocal { get; }
}
