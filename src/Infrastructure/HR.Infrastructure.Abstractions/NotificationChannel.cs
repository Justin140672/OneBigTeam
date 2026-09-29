namespace HR.Infrastructure.Abstractions;

[Flags]
public enum NotificationChannel
{
    InApp = 1,
    Email = 2,
    Both  = InApp | Email,
}
