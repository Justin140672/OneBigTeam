using HR.Modules.Probation.Domain;

namespace HR.Modules.Probation.Services;

internal static class ProbationReviewAssignment
{
    public static Guid? ResolveTaskAssignee(
        ProbationRecord record, ProbationReviewType reviewType, IReadOnlyList<Guid> hrAdministratorIds)
    {
        if (reviewType == ProbationReviewType.HrReview)
        {
            if (hrAdministratorIds.Count == 0)
                return null;

            return hrAdministratorIds.OrderBy(id => id).First();
        }

        return record.ManagerEmployeeId;
    }

    public static IReadOnlyList<Guid> ResolveNotificationRecipients(
        ProbationRecord record, ProbationReviewType reviewType, IReadOnlyList<Guid> hrAdministratorIds)
    {
        if (reviewType == ProbationReviewType.HrReview)
            return hrAdministratorIds;

        return [record.ManagerEmployeeId];
    }
}
