# CodeQL alert dispositions

Status: prepared 2026-09-27 for ticket "[P2] Restore a clean CodeQL status and disposition the
remaining alerts with evidence". Repository: `Justin140672/OneBigTeam`.

This document is the evidence record for every open CodeQL alert. Each alert is either **fixed by a
commit** (it closes by itself when the next default-branch analysis no longer finds it) or
**dismissed as a false positive**, with a regression test or typed invariant that would fail if the
justification stopped being true.

Nothing on GitHub has been changed yet. Every GitHub-side action below is a command for the
repository owner to run after review.

> **Location caveat.** The GitHub CLI was not available when this was written, so alert numbers,
> rules and locations come from the ticket text and the code. Before dismissing anything, confirm each
> alert's rule and file/line with `gh api repos/Justin140672/OneBigTeam/code-scanning/alerts/<n>`
> (see [Verification](#post-push-verification)). If an alert points somewhere other than the location
> listed here, stop and re-evaluate it instead of dismissing it.

## 1. "CodeQL is reporting errors": the Python configuration

**Cause.** Commit `258212db` deleted the repository's only Python file
(`.tmp_architecture/create_architecture_doc.py`). Default setup still lists `python`, so that
language's analysis fails with "No Python code found". C#, JavaScript/TypeScript and Actions analyse
normally.

**Code-side fix (this commit).** `.tmp_architecture/` is now in `.gitignore`, so scratch output from
local architecture-doc tooling can't re-introduce a stray `.py` file.

**Decision: keep default setup; don't add an advanced workflow.**

| Option | Pros | Cons |
|---|---|---|
| Keep default setup, drop Python via the API (chosen) | One API call; GitHub keeps query packs and runners up to date; nothing to maintain in the repo | Language list is repo configuration, not code (recorded here instead) |
| Add `.github/workflows/codeql.yml` (csharp, javascript-typescript, actions) | Configuration is versioned; build mode and queries can be customised | Conflicts with default setup: GitHub rejects advanced-workflow SARIF uploads while default setup is enabled, so default setup must be disabled first; the workflow and its pinned actions then need ongoing maintenance |

An advanced workflow only adds value if we need custom queries or a manual C# build. We need
neither, so it isn't added.

**Command to run (owner approval required):**

```bash
gh api -X PATCH repos/Justin140672/OneBigTeam/code-scanning/default-setup \
  -f state=configured \
  -f 'languages[]=csharp' \
  -f 'languages[]=javascript-typescript' \
  -f 'languages[]=actions'
```

Check the result (the response includes a `run_id`/`run_url` for the triggered analysis):

```bash
gh api repos/Justin140672/OneBigTeam/code-scanning/default-setup --jq '{state, languages, query_suite, updated_at}'
```

## 2. Alert register

Test paths are relative to the repository root.

### Fixed by commit: do not dismiss

These close automatically once the fixing commit is on `main` and the next analysis runs. Dismissing
them would hide a regression if the fix were ever reverted.

| Alert | Rule | Location | Fix | Regression evidence |
|---|---|---|---|---|
| **#61** | `cs/exposure-of-private-information` | Identity module operational logs (`CreatePlatformAdministrator`, `SendInvite`, `ResetPlatformAdministratorMfa`, `SignUp` handlers) | `646493dd` stops logging email addresses and email-bearing exceptions | `tests/HR.Modules.Identity.Tests/IdentityOperationalLogEmailExposureTests.cs`, `tests/HR.Modules.Identity.Tests/IdentityOperationalLogEmailGuardTests.cs` |
| **#60** | `cs/log-forging` | `src/HR.Api/RateLimiting/IdentityRateLimiting.cs` (`OnRejected` logged `HttpContext.Request.Path`) | This commit: the log carries only the matched endpoint's **registered rate-limit policy name** (`RateLimitRejectionLogging.ResolvePolicyName`); anything unregistered is logged as `unknown`. The path and query string are never logged. | `tests/HR.Integration.Tests/RequestTargetLogForgingTests.cs`: runs the real `OnRejected` delegate with raw CR/LF, percent-decoded `%0D%0A`, literal `%0D%0A`, U+2028 and NUL/ESC paths plus a secret-bearing query string. It asserts exactly one log record, `RateLimitPolicy=identity-login`, and that none of the hostile text reaches the message, state or scopes. |
| **#68** | `cs/log-forging` | `src/HR.Api/Authentication/SupabaseJwtBearerConfiguration.cs` (`OnTokenValidated` logged `HttpContext.Request.Path` when rejecting a revoked session) | This commit: `LogRevokedSessionRejected` logs a fixed event name `AuthEvent=session_revoked`, the typed `Sub` (Guid) and `TokenIssuedAt`, and the matched endpoint's **route template** (for example `api/companies/{companyId}/employees`), or `(unmatched)`. The raw target is never logged. | `tests/HR.Integration.Tests/RequestTargetLogForgingTests.cs`: same hostile-path matrix. It asserts one record, the fixed event name, the server-defined template, and no request-target text. |
| Admin-support stored XSS | `cs/web/xss` | `src/HR.Admin.Web/Components/Pages/SupportRequestDetails.razor` (support response body rendered raw) | `6e0bf317`: bodies are sanitised on write (`SupportResponse.Create`), backfilled (`SupportResponseBodySanitisationJob`), sanitised again on render, and HR.Admin.Web has a CSP | `tests/HR.SharedKernel.Tests/SupportHtmlSanitizerTests.cs`, Support handler/domain/backfill tests, `tests/HR.Web.E2E.Tests/Tests/AdminSupportConversationXssTests.cs` |

**#61 must close through its fix, not by dismissal.** If it is still open after the post-push
analysis, the fix didn't cover the flagged path. Reopen the investigation rather than dismissing it.

**If CodeQL raises a new XSS alert on the sanitised `MarkupString` sink.** Both
`src/HR.Admin.Web/Components/Pages/SupportRequestDetails.razor:135` and
`src/HR.Web/Components/Pages/Support/SupportRequestDetail.razor:116` render
`(MarkupString)SupportHtmlSanitizer.Sanitize(response.BodyHtml)`. CodeQL doesn't model
`HR.SharedKernel.Html.SupportHtmlSanitizer` (an allow-list HTML sanitiser), so it may still report
`cs/web/xss` at those lines. The value is sanitised on write, on backfill and again on render, and
HR.Admin.Web also has a strict CSP, so such an alert is a **false positive**. Evidence:
`tests/HR.SharedKernel.Tests/SupportHtmlSanitizerTests.cs` and the Support tests added in `6e0bf317`.
First confirm the flagged sink is exactly that sanitised expression, then dismiss with:

```bash
gh api -X PATCH repos/Justin140672/OneBigTeam/code-scanning/alerts/<NEW_ALERT_NUMBER> \
  -f state=dismissed -f dismissed_reason="false positive" \
  -f dismissed_comment="Sink is (MarkupString)SupportHtmlSanitizer.Sanitize(...): allow-list sanitiser applied on write, backfill and render (6e0bf317), plus CSP. CodeQL does not model the sanitiser. Evidence: tests/HR.SharedKernel.Tests/SupportHtmlSanitizerTests.cs"
```

### False positives: dismiss with evidence

`dismissed_comment` is limited to 280 characters; each comment below fits.

#### #59: `cs/web/xss`, `src/HR.Web/Services/SupportService.cs` (`SubmitSupportRequestAsync`, `new StringContent(FormText.Required(description))`)

**Justification.** `StringContent` here is an outbound `multipart/form-data` field on the HttpClient
request to HR.Api. It is request serialisation, not an HTTP response written to a browser. The support
request description is only ever displayed through Razor text interpolation (`@_detail.Description`
in `src/HR.Web/Components/Pages/Support/SupportRequestDetail.razor` and
`src/HR.Admin.Web/Components/Pages/SupportRequestDetails.razor`), which HTML-encodes. It is never
passed through `MarkupString`.

**Evidence.**
- `tests/HR.Web.Tests/SupportRequestDescriptionEncodingTests.cs` has three checks:
  - a source invariant: the description is rendered via `@_detail.Description`, with no `MarkupString`;
  - an `HtmlRenderer` proof: a script/img payload renders as `&lt;script&gt;`/`&lt;img`;
  - a service check: the description is sent as a `text/plain` form field, verbatim.
- `tests/HR.Admin.Web.Tests/SupportRequestDescriptionEncodingTests.cs` runs the same source-invariant
  and rendering proof for the admin page.

```bash
gh api -X PATCH repos/Justin140672/OneBigTeam/code-scanning/alerts/59 \
  -f state=dismissed -f dismissed_reason="false positive" \
  -f dismissed_comment="StringContent is an outbound multipart form field, not a response. Description is rendered only via Razor @text interpolation (HTML-encoded), never MarkupString. Tests: tests/HR.Web.Tests/SupportRequestDescriptionEncodingTests.cs (+ HR.Admin.Web.Tests)"
```

#### #34: `cs/exposure-of-private-information` / `cs/cleartext-storage` family, `src/Modules/HR.Modules.Identity/Features/RequestPasswordReset/Handler.cs:58`

**Justification.** The flagged log is `"Password reset email dispatch attempted. EmailSent={EmailSent}"`.
`emailSent` is the `bool` returned by `IPasswordResetEmailSender.SendAsync`. CodeQL taints it because
the call's arguments include the email address and recovery URL, but a boolean carries none of that
data. The handler never logs the address, the action URL or the token.

**Evidence.** `tests/HR.Modules.Identity.Tests/RequestPasswordResetLogExposureTests.cs` covers the
sent, not-sent and no-profile paths. It asserts that no captured channel (message, state, scopes,
exception) contains the email, its local part, the recovery token, `token=`, the action URL, the
redirect URL or the user agent, and that `EmailSent` is exactly `True`/`False`.

```bash
gh api -X PATCH repos/Justin140672/OneBigTeam/code-scanning/alerts/34 \
  -f state=dismissed -f dismissed_reason="false positive" \
  -f dismissed_comment="Logged value is the bool EmailSent result, not the email, action URL or token. Test proves none of them reach any log channel: tests/HR.Modules.Identity.Tests/RequestPasswordResetLogExposureTests.cs"
```

#### #55, #56, #57, #62: `cs/log-forging`, `src/Modules/HR.Modules.Identity/Jobs/AccountDisablementJob.cs` (log statements at lines 63, 71, 93, 117) and, if flagged, `AccountDisablementReconciliationJob.cs` (lines 88, 96)

**Classification.** Hangfire job arguments and entity fields, all typed `System.Guid`:
`accountDisablementId`, `companyId` and `claimedBy`, plus `AccountDisablement.Id/CompanyId/ApplicationUserId/EmployeeId`.
These are server-generated account-disablement and company identifiers. A `Guid` formats only as
hex digits and hyphens, so it can't carry CR/LF or other forging characters. The only non-Guid value,
the reconciliation job's `{Reason}`, is `AccountDisablement.Status`: a private-set field restricted
to four lowercase constants.

**Evidence.** `tests/HR.Modules.Identity.Tests/AccountDisablementLogIdentifierInvariantTests.cs`
checks that:
- every `ProcessAsync` overload takes only `Guid` parameters;
- the four identifier properties are `Guid`;
- `Status` has no public setter, and every state transition keeps it within the closed constant set.

```bash
for n in 55 56 57 62; do
gh api -X PATCH repos/Justin140672/OneBigTeam/code-scanning/alerts/$n \
  -f state=dismissed -f dismissed_reason="false positive" \
  -f dismissed_comment="Logged values are System.Guid job args/entity ids (server-generated account-disablement/company ids) - hex+hyphen only, cannot forge log lines. Typed invariant: tests/HR.Modules.Identity.Tests/AccountDisablementLogIdentifierInvariantTests.cs"
done
```

#### #63, #64, #65: `cs/log-forging`, `src/Modules/HR.Modules.Support/Services/UploadedAttachmentCleanupScope.cs` (lines 72, 125, 132: `{StorageKey}` / `{CorrelationId}`)

**Justification.**

*Storage keys.* These are generated server-side by
`src/Infrastructure/HR.Infrastructure/Storage/{Local,Supabase}SupportAttachmentStorageService.cs`
as `support/{companyId}/{requestId}/{Guid:N}{extension}`. The extension has already passed
`SupportAttachmentValidator`'s allow-list (`.pdf .png .jpg .jpeg .txt .log`), and the original file
name is never embedded. Only a redacted ≤12-character tail is logged. As defence in depth, this
commit makes `RedactStorageKey` replace any character outside `[A-Za-z0-9._-]` with `?`.

*Correlation IDs.* These come from `IExecutionContext`, which is established by
`CorrelationIdMiddleware`: at most 128 characters from `[A-Za-z0-9._:-]`, otherwise a fresh GUID. This
commit also fixes that regex's anchor from `$` to `\z`. In .NET, `$` also matches before a trailing
`\n`, so `abc\n` used to pass. Kestrel already rejects raw CR/LF in header values, so this was not
exploitable over HTTP, but the allow-list now holds on its own.

**Evidence.**
- `tests/HR.Modules.Support.Tests/SupportStorageKeyLogSafetyTests.cs`:
  - redaction output for real and hostile keys;
  - validator rejection of CR/LF, NUL, `%0D%0A` and disallowed extensions.
- `tests/HR.Infrastructure.Tests/Storage/SupportStorageKeyShapeTests.cs`: generated key shape
  `^support/{companyId}/{requestId}/[0-9a-f]{32}\.pdf\z`, with no original file name.
- `tests/HR.Infrastructure.Tests/CorrelationIdLogSafetyTests.cs`:
  - `IsAcceptable` rejects CR, LF, trailing LF, NUL, tab, space, U+2028, `%0D%0A`, and more than 128
    characters;
  - the middleware replaces a hostile header with a GUID.

```bash
for n in 63 64 65; do
gh api -X PATCH repos/Justin140672/OneBigTeam/code-scanning/alerts/$n \
  -f state=dismissed -f dismissed_reason="false positive" \
  -f dismissed_comment="StorageKey = server GUID + allow-listed ext, logged redacted/char-filtered; CorrelationId passed middleware allow-list (<=128, [A-Za-z0-9._:-]). Tests: HR.Modules.Support.Tests/SupportStorageKeyLogSafetyTests.cs, HR.Infrastructure.Tests/CorrelationIdLogSafetyTests.cs"
done
```

#### #66: `cs/log-forging`, `src/Modules/HR.Modules.Recruitment/Services/CandidateDocumentUploadStaging.cs` (`CompensateAsync` warnings, lines 97 and 120: `{StorageKeySuffix}`, `{CorrelationId}`)

**Justification.** Candidate-document storage keys are built by
`{Local,Supabase}CandidateDocumentStorageService.GenerateStorageKey` as
`{companyId}/{candidateId}/{Guid:N}{extension}`. On every upload path
(`UploadCandidateDocument`, `CandidateApplicationIntake`, `ApplyForInternalVacancy`), the extension
passes `CandidateDocumentUploadStaging.ValidateFile`'s allow-list before a key is generated. Only a
redacted, character-filtered ≤12-character suffix is logged, and the correlation ID follows the same
middleware policy as above. The malware-scan code added in `4aa5ece4`
(`ScanCandidateDocumentJob`, `ReconcileCandidateDocumentScansJob`) logs only document, candidate and
company GUIDs, attempt counts, and `ScanFailureReason`. That value comes from a closed set of
constants or from `CandidateDocumentScanFailureReasons.SanitiseThreatName`, which trims the name and
applies an allow-list; it never includes storage keys.

**Evidence.** `tests/HR.Modules.Recruitment.Tests/CandidateDocumentStorageKeyLogSafetyTests.cs`
covers:
- redaction of real and hostile keys;
- `ValidateFile` rejecting CR/LF, control-character and percent-encoded extensions;
- `GenerateStorageKey` shape for both the Local and Supabase implementations;
- a `CompensateAsync` failed-delete warning whose suffix is safe and whose message and state contain
  no control characters and no full key.

```bash
gh api -X PATCH repos/Justin140672/OneBigTeam/code-scanning/alerts/66 \
  -f state=dismissed -f dismissed_reason="false positive" \
  -f dismissed_comment="Storage key = {companyId}/{candidateId}/{server GUID}{allow-listed ext}; only a redacted, char-filtered <=12-char suffix is logged. Tests: tests/HR.Modules.Recruitment.Tests/CandidateDocumentStorageKeyLogSafetyTests.cs"
```

#### #67: `cs/log-forging`, `src/Shared/HR.SharedKernel/IntegrationEventPublisher.cs:112` (handler-failure log: `CorrelationId`, `MessageId`, `CausationId`)

**Justification.**
- `MessageId` and `CausationId` are `Guid`/`Guid?` values minted in code
  (`ExecutionContextInfo.NewRoot`/`CausedBy`) or restored from Guid-typed columns.
- `CorrelationId` is either a GUID string or a caller value that already passed
  `CorrelationIdMiddleware`'s allow-list, now anchored with `\z`.
- Handler and event names are `Type.Name`.

**Evidence.** `tests/HR.Infrastructure.Tests/CorrelationIdLogSafetyTests.cs` runs a failing
`IntegrationEventPublisher` handler inside the real middleware, with CR/LF headers and a valid
prefixed header. It asserts:
- exactly one error record;
- `CorrelationId` matches `^[A-Za-z0-9._:-]{1,128}\z`;
- the raw `MessageId`/`CausationId` state values are `Guid`;
- the scope carries the same values;
- no CR/LF or injected text appears anywhere.

```bash
gh api -X PATCH repos/Justin140672/OneBigTeam/code-scanning/alerts/67 \
  -f state=dismissed -f dismissed_reason="false positive" \
  -f dismissed_comment="CorrelationId passed CorrelationIdMiddleware allow-list (<=128, [A-Za-z0-9._:-], \z-anchored); MessageId/CausationId are code-minted Guids. Test: tests/HR.Infrastructure.Tests/CorrelationIdLogSafetyTests.cs"
```

## Post-push verification

1. Push the commits (owner action) and let the default-setup analysis on `main` finish:
   `gh run list --workflow "CodeQL" --branch main --limit 3` (default setup runs appear as
   "CodeQL" dynamic workflow runs), or check the Security → Code scanning → Tool status page.
2. Apply the default-setup language change (section 1) and confirm that
   `gh api repos/Justin140672/OneBigTeam/code-scanning/default-setup --jq .languages` returns
   `["actions","csharp","javascript-typescript"]` in any order. The tool-status page should no
   longer say "CodeQL is reporting errors".
3. Confirm that the fixed alerts closed on their own:
   ```bash
   for n in 60 61 68; do gh api repos/Justin140672/OneBigTeam/code-scanning/alerts/$n --jq '"\(.number) \(.state) \(.fixed_at // "-")"'; done
   ```
   All three should report `fixed`. If any is still `open`, investigate; **do not dismiss it**.
4. Re-read each false-positive alert and confirm its rule and location match this document before
   running its dismissal command:
   ```bash
   for n in 34 55 56 57 59 62 63 64 65 66 67; do gh api repos/Justin140672/OneBigTeam/code-scanning/alerts/$n --jq '"\(.number) \(.state) \(.rule.id) \(.most_recent_instance.location.path):\(.most_recent_instance.location.start_line)"'; done
   ```
5. Run the dismissal commands, then confirm that nothing unexpected remains open:
   ```bash
   gh api "repos/Justin140672/OneBigTeam/code-scanning/alerts?state=open&per_page=100" --jq '.[] | "\(.number) \(.rule.id) \(.most_recent_instance.location.path):\(.most_recent_instance.location.start_line)"'
   ```
   Any new alert, including an XSS alert on the sanitised `MarkupString` sink, gets its own review
   entry in this document before it is dismissed.
