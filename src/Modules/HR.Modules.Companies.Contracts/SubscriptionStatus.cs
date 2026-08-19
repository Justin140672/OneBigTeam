namespace HR.Modules.Companies.Contracts;

// Trial/subscription lifecycle status owned by HR.Modules.Companies' CustomerSubscription
// aggregate. Exposed here (rather than kept module-internal) because it is returned across the
// module boundary by ISubscriptionStatusReader, following the same precedent as
// NoticePeriodUnit/EmployeeUserAccountStatus above.
public enum SubscriptionStatus
{
    Trial = 0,
    TrialExpired = 1,
    Active = 2,
    PastDue = 3,
    Canceled = 4,
}
