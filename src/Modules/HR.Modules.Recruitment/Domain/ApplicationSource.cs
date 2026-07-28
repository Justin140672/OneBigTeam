namespace HR.Modules.Recruitment.Domain;

// Nullable/defaults-to-null on Application for backward compatibility with existing applications
// created before this concept existed (ticket #78). Unspecified is included as an explicit value
// (rather than relying purely on null) so that a user can deliberately record "we don't know/care
// how this candidate found us" as distinct from "not yet asked" (null).
internal enum ApplicationSource
{
    Unspecified,
    Direct,
    Referral,
    ExternalRecruiter,
    JobBoard,
    CareersSite,
}
