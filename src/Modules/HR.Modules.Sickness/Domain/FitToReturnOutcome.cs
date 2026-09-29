namespace HR.Modules.Sickness.Domain;

/// <summary>
/// The structured clinical-fitness outcome recorded on a return-to-work review (SICK-03).
/// Deliberately a small closed set of business outcomes rather than free text — the review
/// still allows free-text <see cref="ReturnToWorkReview.AdjustmentDetails"/> and
/// <see cref="ReturnToWorkReview.Notes"/> fields for context, but the outcome itself must be
/// one of these values so downstream logic (e.g. reopening the sickness record) can be driven
/// by a known enum rather than parsing text.
/// </summary>
internal enum FitToReturnOutcome
{
    Fit,

    FitWithAdjustments,

    NotFit
}
