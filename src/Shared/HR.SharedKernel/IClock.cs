namespace HR.SharedKernel;

public interface IClock
{
    DateTime UtcNow { get; }
}
