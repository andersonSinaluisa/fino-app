namespace Nexo.Domain.Common;

/// <summary>
/// Time is an input, never an ambient global: every aggregate receives the
/// current instant so timezone and month-boundary behaviour is testable.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class FixedClock(DateTimeOffset instant) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = instant;
}
