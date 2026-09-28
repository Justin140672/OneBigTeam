namespace HR.SharedKernel;

public interface IClockProvider
{
    DateTimeOffset UtcNow { get; }
}
