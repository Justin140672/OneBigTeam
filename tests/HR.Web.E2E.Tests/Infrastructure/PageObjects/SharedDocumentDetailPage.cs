using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the shared company document detail page
/// (/companies/{companyId}/shared-documents/{documentId}), scoped to the Version History grid,
/// the Publish flow, the Archive flow, the "Mark Expired" flow (see SharedDocumentExpireTests),
/// the Complete Review flow (CompleteSharedCompanyDocumentReviewDialog.razor —
/// see SharedDocumentCompleteReviewTests), the Acknowledgement card's "Edit" flow (used by
/// CompanyDocumentsTabTests to set up documents with acknowledgement requirements before
/// publishing), the Audience card's summary/edit-dialog affordances, the Document Metadata
/// card's "Edit" flow (covering Review Frequency — see SharedDocumentReviewFrequencyTests — and
/// Review Owner — see SharedDocumentReviewOwnerTests), and navigation to the
/// acknowledgement-progress screen.
/// </summary>
public sealed class SharedDocumentDetailPage(IPage page, string baseUrl)
{
    // The redesign moved Publish and "Record review" (formerly "Review Document") into the header
    // action group, and Archive / Mark Expired / Audit History into a Syncfusion SfDropDownButton
    // ("More actions" — see BuildMoreActionsItems/HandleMoreActionSelectedAsync in
    // SharedDocumentDetail.razor). Publish/Record review are scoped to the header's own
    // ".doc-detail-actions-group" rather than a bare icon filter: the "Review History" tab pane
    // now also renders its own "Record review" button (same fa-clipboard-check icon/text, gated on
    // Session.CanManageSharedDocuments) once that tab is selected, which an unscoped icon/text
    // filter would collide with.
    private ILocator HeaderActionsGroup => page.Locator(".doc-detail-actions-group");
    private ILocator PublishHeaderButton => HeaderActionsGroup.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-paper-plane") });

    // The "Record review" header button also carries the fa-clipboard-check icon that the
    // Acknowledgement overview-card's header uses, but that header icon lives on a plain <span>
    // (not a button) — scoping to the header actions group is still needed on top of that for the
    // Review History tab's own "Record review" button, see the remarks above.
    private ILocator ReviewHeaderButton => HeaderActionsGroup.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-clipboard-check") });

    // "Edit details" is a unique button name on the page now (Audience/Acknowledgement cards use
    // their own distinct "Edit audience"/"Edit acknowledgement settings" names — see the renamed
    // locators below), so this no longer needs the old .First-based disambiguation against a
    // shared generic "Edit" name.
    //
    // NOT Exact — confirmed via a captured DOM dump (diag/*_shareddoc-header-missing.html) that
    // this specific SfButton carries aria-label="Edit details for {Title}" (see
    // SharedDocumentDetail.razor). Per the WAI-ARIA accessible-name computation algorithm, a
    // non-empty aria-label completely OVERRIDES an element's visible text content for accessible-
    // name purposes — Playwright's GetByRole Name match is against that computed name, not what's
    // on screen. So the button's real accessible name is "Edit details for {Title}", never exactly
    // "Edit details", and Exact=true could never match it — the button was visible and clickable
    // the entire time; every prior "fix" to GoToAsync's timing/retries was chasing a symptom that
    // couldn't be timing-related, since the target literally didn't have the name being searched
    // for. Non-exact matching is substring-based in Playwright, so "Edit details" now matches
    // against "Edit details for {Title}" correctly.
    private ILocator EditMetadataHeaderButton => page.GetByRole(AriaRole.Button, new() { Name = "Edit details" });
    private ILocator EditMetadataDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Document Metadata" });

    private ILocator PublishDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Publish Document" });
    private ILocator ArchiveDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Archive Document" });
    private ILocator ExpireDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Mark Document as Expired" });
    private ILocator ReviewDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Complete Review" });

    // Scoped to the "Acknowledgement" overview-card, though "Edit acknowledgement settings" is
    // itself now a unique name on the page.
    private ILocator AcknowledgementCard => page.Locator(".overview-card").Filter(new() { HasText = "Acknowledgement" }).First;
    private ILocator EditAcknowledgementDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Acknowledgement Settings" });

    // Scoped to the "Audience" overview-card, though "Edit audience" is itself now a unique name
    // on the page.
    private ILocator AudienceCard => page.Locator(".overview-card").Filter(new() { HasText = "Audience" }).First;

    // ── "More actions" overflow menu (Archive / Mark Expired / Audit History) ──────────────────
    // Follows the same open-menu/wait-for-item/click-item pattern already established for the
    // Employee page's own "More actions" menu — see StartLeavingProcessDialog.OpenAsync and
    // EmployeeEditPage.OpenMoreActionsMenuAsync/ClickViewOrganisationChartMenuItemAsync.
    private ILocator MoreActionsButton => page.GetByRole(AriaRole.Button, new() { Name = "More actions" });

    /// <summary>
    /// Maps a "More actions" item's display name to its stable DropDownMenuItem.Id from
    /// BuildMoreActionsItems (SharedDocumentDetail.razor) — "archive"/"expire"/"audit" become the
    /// rendered &lt;li&gt;'s literal id attribute, same as BulkUpdateMenu.razor's "hr-bulk-selected"
    /// (see EmployeeListPage.ClickBulkUpdateAsync's remarks for that established pattern).
    /// </summary>
    private static string MoreActionsItemId(string itemName) => itemName switch
    {
        "Archive" => "archive",
        "Mark Expired" => "expire",
        "Audit History" => "audit",
        _ => throw new ArgumentOutOfRangeException(nameof(itemName), itemName, "Unknown 'More actions' item name."),
    };

    /// <summary>
    /// Opens the "More actions" overflow menu and clicks the named item (e.g. "Archive",
    /// "Mark Expired", "Audit History"), retrying the open if the popup doesn't mount in time —
    /// same "freshly mounted SfDropDownButton can silently swallow a same-tick click" race
    /// documented on StartLeavingProcessDialog.OpenAsync.
    ///
    /// Located by the item's stable id (see <see cref="MoreActionsItemId"/>), NOT by
    /// role+accessible-name: BuildMoreActionsItems() is an inline method call re-evaluated on
    /// EVERY component re-render (Blazor doesn't memoize it), so this dropdown's Items list can
    /// rebuild more than once in quick succession right after the page's data load completes.
    /// Each rebuild briefly re-renders the popup, and a role+name query issued mid-rebuild can
    /// observe a transient state with no matching item at all — not just "swallowed click" (the
    /// button click itself lands fine; the popup opens; it's the item set inside it that's
    /// mid-flux) — which the original retry loop's 3×5s + 10s budget could still exhaust if the
    /// rebuilds keep recurring. An id-based locator auto-waits through that churn and resolves
    /// against whichever render eventually wins, same fix already proven for
    /// EmployeeListPage.ClickBulkUpdateAsync's "hr-bulk-selected" item.
    /// </summary>
    private async Task ClickMoreActionsItemAsync(string itemName)
    {
        var menuItem = page.Locator($"#{MoreActionsItemId(itemName)}");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await MoreActionsButton.ClickAsync();
            try
            {
                await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            }
            catch (TimeoutException)
            {
                await page.Keyboard.PressAsync("Escape");
                continue;
            }

            await menuItem.ClickAsync();
            return;
        }

        // Final attempt without swallowing the exception, so a genuine failure still surfaces —
        // but with the popup's actual current contents attached, so a failure here says WHAT was
        // rendered instead of just "item not found" (distinguishes "popup never opened"/"still
        // empty" from "opened with a different item set than expected").
        await MoreActionsButton.ClickAsync();
        try
        {
            await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            var popupText = await page.Locator(".e-dropdown-popup").IsVisibleAsync()
                ? await page.Locator(".e-dropdown-popup").InnerTextAsync()
                : "(popup not visible)";
            throw new TimeoutException(
                $"'More actions' item '{itemName}' (#{MoreActionsItemId(itemName)}) never appeared after 3 retries. " +
                $"Popup contents at final failure: {popupText}");
        }

        await menuItem.ClickAsync();
    }

    /// <summary>
    /// True if the named item (e.g. "Archive", "Mark Expired") is present in the (currently
    /// closed) "More actions" menu — BuildMoreActionsItems only includes Archive/Mark Expired
    /// while Status is Draft or Published, and always includes "Audit History". Opens the menu to
    /// check, then closes it again via Escape so callers aren't left with an open popup.
    /// </summary>
    private async Task<bool> HasMoreActionsItemAsync(string itemName)
    {
        await MoreActionsButton.ClickAsync();
        bool visible;
        try
        {
            // Id-based, not role+name — see ClickMoreActionsItemAsync's remarks on why role+name
            // can miss a mid-rebuild popup.
            await page.Locator($"#{MoreActionsItemId(itemName)}")
                .WaitForAsync(new() { Timeout = 3_000 });
            visible = true;
        }
        catch (TimeoutException)
        {
            visible = false;
        }

        await page.Keyboard.PressAsync("Escape");
        return visible;
    }

    public async Task GoToAsync(Guid companyId, Guid documentId)
    {
        // SharedDocumentDetail.razor has no grid at all (".e-grid" — an earlier wait condition here
        // — never appears on this page), so that wait provided no real readiness signal: it either
        // matched nothing until a 20s timeout, or matched by coincidence, without confirming
        // _detail had actually finished loading. The page's own three render states are: loading
        // (HrLoadingIndicator), not-found (".alert-danger" — GetSharedCompanyDocumentAsync returned
        // null), or loaded (h1.doc-detail-title plus the "More actions" SfDropDownButton, whose
        // Items are computed from _detail).
        //
        // Most callers navigate here immediately after uploading a document (GetUploadedDocumentIdAsync
        // reads the id straight off the just-appeared list row), which is a genuine read-after-write
        // race: this page's own GET can transiently 404 a document whose write the list grid already
        // reflects. Treating a lone ".alert-danger" sighting as this page having "loaded" (as an
        // earlier version of this method did) let that transient 404 through immediately — the old,
        // technically-wrong ".e-grid" wait had incidentally masked this by always burning close to a
        // full 20s before ever checking, giving the backend time to catch up. Retry the navigation a
        // few times before accepting ".alert-danger" as final, so a genuine not-found (the document
        // really doesn't exist) still surfaces reliably, but a transient one resolves instead of
        // wrongly reporting the document missing and leaving every subsequent locator in the caller
        // (e.g. "Edit details") timing out against a page that was never going to render them.
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await page.GotoAsync($"{baseUrl}/companies/{companyId}/shared-documents/{documentId}");
            await page.WaitForSelectorAsync("h1.doc-detail-title, .alert-danger", new() { Timeout = 20_000 });

            if (await page.Locator("h1.doc-detail-title").IsVisibleAsync())
            {
                await DumpDiagnosticsIfHeaderActionsMissingAsync();
                return;
            }

            if (attempt < 4)
                await page.WaitForTimeoutAsync(500 * attempt);
        }

        // Every attempt landed on ".alert-danger" ("Document was not found") — fail loudly here
        // instead of silently returning with the caller left on that page. A silent return
        // previously made a genuine/persistent not-found masquerade as a much-later, unrelated-
        // looking timeout on whatever locator the caller tried next (e.g. "Edit details" never
        // appearing, since that button only exists in the loaded branch) — which is exactly the
        // wrong diagnostic to chase. This documentId/companyId pairing is either genuinely wrong
        // (a caller bug upstream, e.g. GetUploadedDocumentIdAsync parsed something other than the
        // real id) or the read-after-write race is worse than 4 retries can cover.
        throw new TimeoutException(
            $"SharedDocumentDetailPage.GoToAsync: document {documentId} (company {companyId}) still shows " +
            "'Document was not found' after 4 attempts. Either the id is wrong or the document " +
            "genuinely isn't resolvable via GetSharedCompanyDocumentAsync — this is NOT a rendering-timing issue.");
    }

    /// <summary>
    /// Best-effort, non-throwing diagnostic capture. A captured DOM dump already explained the
    /// "Edit details" case: that SfButton carries aria-label="Edit details for {Title}", which per
    /// WAI-ARIA accessible-name computation OVERRIDES its visible text entirely — an Exact=true
    /// role+name match could never succeed against it regardless of timing, which is why several
    /// rounds of timing/retry fixes to GoToAsync had no effect (EditMetadataHeaderButton is now
    /// non-exact to fix this — see its own remarks). "Edit audience"/"Edit acknowledgement
    /// settings" carry no aria-label in source, so they're presumed unaffected by that specific
    /// bug, but their failures haven't been directly evidenced yet — this checks all three
    /// (non-exact, matching the now-fixed locator strategy) and captures a screenshot + full DOM if
    /// any is still genuinely missing, so a recurrence points at real, new evidence rather than a
    /// re-guess.
    /// </summary>
    private async Task DumpDiagnosticsIfHeaderActionsMissingAsync()
    {
        try
        {
            var editDetails = page.GetByRole(AriaRole.Button, new() { Name = "Edit details" });
            var editAudience = page.GetByRole(AriaRole.Button, new() { Name = "Edit audience" });
            var editAcknowledgement = page.GetByRole(AriaRole.Button, new() { Name = "Edit acknowledgement settings" });

            // Give it a brief, bounded moment in case of a genuine late re-render, without adding
            // meaningful cost when everything's already there.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                if (await editDetails.IsVisibleAsync() && await editAudience.IsVisibleAsync()
                    && await editAcknowledgement.IsVisibleAsync())
                {
                    return; // All present as expected — nothing to capture.
                }
                await page.WaitForTimeoutAsync(200);
            }

            var dir = Path.Combine(AppContext.BaseDirectory, "diag");
            Directory.CreateDirectory(dir);
            var stamp = $"{DateTime.UtcNow:HHmmss_fff}_{Guid.NewGuid().ToString("N")[..6]}_shareddoc-header-missing";
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{stamp}.png"), FullPage = true });
            await File.WriteAllTextAsync(
                Path.Combine(dir, $"{stamp}.html"),
                $"URL: {page.Url}\n\n=== DOM ===\n{await page.ContentAsync()}");
        }
        catch
        {
            // Diagnostics only — never let capture failure affect the caller.
        }
    }

    /// <summary>
    /// Navigates to the acknowledgement-progress screen
    /// (SharedDocumentAcknowledgementProgress.razor), reached from this page via the
    /// "Acknowledgement" overview-card's "View Progress" link (only rendered when the document
    /// requires acknowledgement).
    /// </summary>
    public async Task GoToAcknowledgementProgressAsync(Guid companyId, Guid documentId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/shared-documents/{documentId}/acknowledgement-progress");
        await page.WaitForSelectorAsync(".overview-card, .alert-danger", new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Text of the Status badge in the Document Metadata card (e.g. "Draft", "Published", "Archived").</summary>
    public async Task<string> GetStatusAsync() =>
        (await page.Locator("dt:has-text('Status') + dd .badge").InnerTextAsync()).Trim();

    /// <summary>Page header's document title (the &lt;h1&gt; — SharedDocumentDetail.razor renders @_detail.Title there).</summary>
    public async Task<string> GetTitleAsync() =>
        (await page.Locator("h1").First.InnerTextAsync()).Trim();

    // ── "Current document" card (filename/type/size/version, Open/Download/Upload New Version) ──

    /// <summary>The current file's name shown in the "Current document" card.</summary>
    public async Task<string> GetCurrentDocumentFileNameAsync() =>
        (await page.Locator(".doc-current-file-name").InnerTextAsync()).Trim();

    /// <summary>
    /// The current file's meta line (content type · size, plus "Uploaded … by …" once the
    /// current version's own upload metadata is resolvable — see SharedDocumentDetail.razor's
    /// CurrentVersionEntry) shown in the "Current document" card.
    /// </summary>
    public async Task<string> GetCurrentDocumentMetaTextAsync() =>
        (await page.Locator(".doc-current-file-meta").InnerTextAsync()).Trim();

    /// <summary>The "Open" link in the "Current document" card (opens the current version in a new tab).</summary>
    public ILocator CurrentDocumentOpenLink => page.Locator(".doc-current-file-actions a").Filter(new() { HasText = "Open" });

    /// <summary>The "Download" link in the "Current document" card.</summary>
    public ILocator CurrentDocumentDownloadLink => page.Locator(".doc-current-file-actions a").Filter(new() { HasText = "Download" });

    /// <summary>
    /// True if the "Upload New Version" button is present on the "Current document" card —
    /// SharedDocumentDetail.razor only renders it while the document's Status isn't "Archived".
    /// </summary>
    public Task<bool> IsUploadNewVersionButtonVisibleAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Upload New Version" }).IsVisibleAsync();

    /// <summary>Text of the "Category" row in the Document Metadata card.</summary>
    public async Task<string> GetCategoryAsync() =>
        (await page.Locator("dt:has-text('Category') + dd").InnerTextAsync()).Trim();

    /// <summary>
    /// Text of the "Description" row in the Document Metadata card, or null when the row isn't
    /// rendered at all — SharedDocumentDetail.razor only renders this row once Description is
    /// non-blank.
    /// </summary>
    public async Task<string?> GetDescriptionAsync()
    {
        var row = page.Locator("dt:has-text('Description') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Drives the page header's "Edit" button and EditSharedCompanyDocumentMetadataDialog.razor to
    /// change Title, Description, and Category together, then waits for the page to reload its
    /// detail data. <paramref name="categoryLabel"/> is matched against the Category dropdown's
    /// list items (e.g. "Handbook") — same click-open/wait-for-popup/click-item pattern as
    /// SetReviewFrequencyAsync above.
    /// </summary>
    // See the remarks on the FillAsync -> ClearAndTypeAsync switch in EditTitleDescriptionCategoryAsync.
    private async Task ClearAndTypeAsync(ILocator input, string value)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value, new() { Delay = 30 });
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task EditTitleDescriptionCategoryAsync(string title, string description, string categoryLabel)
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Plain FillAsync sets the native input's value and fires a single "input" event, which
        // doesn't reliably replace a Syncfusion SfTextBox's own tracked value — observed as the
        // saved title being the *old* title with the new one appended (e.g. "Test Policy
        // <guid>Updated Handbook <guid>") rather than replaced. Select-all + delete first, same
        // mitigation already applied to CompanyEditPage.TypeIntoTextBoxAsync and
        // EmployeeEditPage.TypeIntoNumericInputAsync for the identical class of race.
        await ClearAndTypeAsync(EditMetadataDialog.GetByPlaceholder("Document title"), title);
        await ClearAndTypeAsync(EditMetadataDialog.GetByPlaceholder("Optional description"), description);

        var categoryGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(page, categoryGroup, categoryLabel);

        // Confirms the Blazor round-trip committed Model.CategoryId, not just that the popup
        // closed client-side (same pattern as SetReviewFrequencyAsync).
        await Assertions.Expect(categoryGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(categoryLabel)), new() { Timeout = 10_000 });

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // Dialog-hidden isn't proof the page's own detail reload has landed in the DOM yet — the
        // h1 can still show the pre-edit title for a beat after the dialog closes. Assert directly
        // on the h1's content (auto-retrying) rather than a spinner-clear proxy, same pattern used
        // for CompleteReview's footer summary elsewhere in this file.
        await Assertions.Expect(page.Locator("h1").First)
            .ToHaveTextAsync(title, new() { Timeout = 15_000 });
    }

    /// <summary>True if "Archive" is present in the "More actions" overflow menu.</summary>
    public Task<bool> IsArchiveButtonVisibleAsync() => HasMoreActionsItemAsync("Archive");

    /// <summary>True if "Mark Expired" is present in the "More actions" overflow menu.</summary>
    public Task<bool> IsExpireButtonVisibleAsync() => HasMoreActionsItemAsync("Mark Expired");

    public Task<bool> IsPublishButtonVisibleAsync() => PublishHeaderButton.IsVisibleAsync();

    public Task<bool> IsReviewButtonVisibleAsync() => ReviewHeaderButton.IsVisibleAsync();

    /// <summary>
    /// Text of the "Next Review Date" row in the Document Metadata card (e.g. "16 August 2026"),
    /// or null when the row isn't rendered at all — SharedDocumentDetail.razor only renders this
    /// row once ReviewDate has a value.
    /// </summary>
    public async Task<string?> GetReviewDateTextAsync()
    {
        var row = page.Locator("dt:has-text('Next Review Date') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Text of the "Review Frequency" row in the Document Metadata card (e.g. "Quarterly" or
    /// "Custom (every 6 months)"), or null when the row isn't rendered at all — SharedDocumentDetail.razor
    /// only renders this row when the frequency isn't "None", so null is the expected result for
    /// a document that has never had a frequency set.
    /// </summary>
    public async Task<string?> GetReviewFrequencyTextAsync()
    {
        var row = page.Locator("dt:has-text('Review Frequency') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Drives the page header's "Edit" button and EditSharedCompanyDocumentMetadataDialog.razor
    /// to set the Review Frequency (and, when selecting "Custom", the custom months value), then
    /// waits for the page to reload its detail data. <paramref name="frequencyLabel"/> is the
    /// dropdown's display label ("None", "Monthly", "Quarterly", "Six Monthly", "Yearly", "Custom"),
    /// not the underlying enum member name (e.g. "Six Monthly" not "SixMonthly"). Whenever
    /// <paramref name="frequencyLabel"/> isn't "None", a Next Review Date is also filled in — the
    /// dialog requires one whenever the frequency isn't "None", otherwise Save is a no-op client-side.
    /// </summary>
    public async Task SetReviewFrequencyAsync(string frequencyLabel, int? customMonths = null)
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Scoped to the "Review Frequency" field's own ".col-md-6" group (rather than by combobox
        // index) since its combobox can render before Category's — Category's is gated behind an
        // async data load while Review Frequency's isn't — same click-open/wait-for-popup/
        // click-item interaction pattern used for Category elsewhere in this test suite (see
        // SharedDocumentUploadTests).
        var reviewFrequencyGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Review Frequency" });
        await DropDownSelector.SelectAsync(page, reviewFrequencyGroup, frequencyLabel);

        // Confirms the Blazor round-trip committed the selection, not just that the popup closed
        // client-side (same pattern as the Review Owner selector below /
        // EmployeeEditPage.SelectManagerAsync). Without this, an immediate Save can race the
        // round-trip.
        await Assertions.Expect(reviewFrequencyGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(frequencyLabel)), new() { Timeout = 10_000 });

        if (frequencyLabel != "None")
        {
            var reviewDateInput = EditMetadataDialog.Locator(".col-md-6")
                .Filter(new() { HasText = "Next Review Date" })
                .Locator(".e-date-wrapper input.e-input");
            await reviewDateInput.ClickAsync();
            await reviewDateInput.FillAsync(DateOnly.FromDateTime(DateTime.Today.AddYears(1)).ToString("dd/MM/yyyy"));
            await page.Keyboard.PressAsync("Tab");
        }

        if (customMonths.HasValue)
        {
            // SfNumericTextBox: a bare FillAsync bypasses its interop entirely — retype for real
            // (same convention as EmployeeEditPage.TypeIntoNumericInputAsync).
            var monthsInput = EditMetadataDialog.Locator(".col-md-6")
                .Filter(new() { HasText = "Custom Frequency" })
                .Locator("input");
            await monthsInput.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await monthsInput.PressSequentiallyAsync(customMonths.Value.ToString());
            await page.Keyboard.PressAsync("Tab");
        }

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // The spinner-clear wait below is a best-effort proxy that's a no-op if the page's own
        // detail-card reload never shows a spinner at all (e.g. a fast/instant re-render) — a
        // caller that immediately reads GetReviewFrequencyTextAsync() right after can still race
        // the actual reload landing (observed: "Yearly" not found right after setting it). Assert
        // directly on the frequency row's own content instead, same pattern already used for
        // EditTitleDescriptionCategoryAsync's title check above, which auto-retries until the real
        // reload lands rather than trusting a loading-state heuristic. "None" has no dt/dd row at
        // all (SharedDocumentDetail.razor only renders it for a non-None frequency), so only assert
        // when setting a real frequency.
        if (frequencyLabel != "None")
        {
            await Assertions.Expect(page.Locator("dt:has-text('Review Frequency') + dd"))
                .ToContainTextAsync(frequencyLabel == "Custom" ? "Custom" : frequencyLabel, new() { Timeout = 15_000 });
        }

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// Text of the "Review Owner" row in the Document Metadata card (e.g. "Marcus Diallo"), or
    /// null when the row isn't rendered at all — SharedDocumentDetail.razor only renders this row
    /// once ReviewOwnerEmployeeId is set, so null is the expected result for a document that has
    /// never had a review owner assigned.
    /// </summary>
    public async Task<string?> GetReviewOwnerTextAsync()
    {
        var row = page.Locator("dt:has-text('Review Owner') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Drives the page header's "Edit" button and EditSharedCompanyDocumentMetadataDialog.razor
    /// to set (or change) the Review Owner via its filterable employee picker, then waits for the
    /// page to reload its detail data. <paramref name="employeeNameFragment"/> is typed into the
    /// dropdown's filter input to trigger the dialog's server-side search (same
    /// OnReviewOwnerFilteringAsync pattern as the Employment tab's Manager picker — see
    /// EmployeeEditPage.SelectManagerAsync) and must match the full display name exactly enough
    /// to resolve to a single result and to assert against the combobox's resulting input value.
    /// </summary>
    public async Task SetReviewOwnerAsync(string employeeNameFragment)
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var reviewOwnerGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Review Owner" });
        await DropDownSelector.SelectAsync(page, reviewOwnerGroup, employeeNameFragment);

        // Confirms the Blazor round-trip committed Model.ReviewOwnerEmployeeId, not just that the
        // popup closed client-side (same reasoning as EmployeeEditPage.SelectManagerAsync).
        await Assertions.Expect(reviewOwnerGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync(employeeNameFragment, new() { Timeout = 10_000 });

        // Even the displayed input value isn't a fully reliable proof of the server-side commit
        // here: Syncfusion's SfDropDownList JS widget updates its own visible <input> (and clear
        // icon) as part of its own client-side selection handling, which can complete slightly
        // ahead of the separate interop call that carries the selection back to Blazor and
        // actually sets Model.ReviewOwnerEmployeeId server-side. Clicking Save in that narrow
        // window submits with the *previous* (still null, on a first-time assignment) value —
        // this is the same class of "visually filled but not yet round-tripped" issue documented
        // on CompanyEditPage.FillNumericAndVerifyAsync, mitigated there with a short buffer before
        // trusting the field's value; do the same here before Save.
        await page.WaitForTimeoutAsync(300);

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// Drives the page header's "Edit" button and EditSharedCompanyDocumentMetadataDialog.razor
    /// to clear the Review Owner by opening its dropdown and selecting the prepended "Not
    /// assigned" sentinel item (Id = Guid.Empty), then waits for the page to reload its detail
    /// data. Replaces the old ShowClearButton ("x" icon) approach, which was removed in favor of
    /// this explicit no-selection list item (see EditSharedCompanyDocumentMetadataDialog.razor's
    /// ReviewOwnerOption list, which prepends a Guid.Empty/"Not assigned" entry).
    /// </summary>
    public async Task ClearReviewOwnerAsync()
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var reviewOwnerGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Review Owner" });
        await DropDownSelector.SelectAsync(page, reviewOwnerGroup, "Not assigned");

        await Assertions.Expect(reviewOwnerGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync("Not assigned", new() { Timeout = 10_000 });

        // Same "visually cleared but not yet round-tripped" concern as SetReviewOwnerAsync above —
        // give the pending ValueChanged interop call a moment to land before Save reads the model.
        await page.WaitForTimeoutAsync(300);

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // Dialog-hidden isn't proof the page's own detail reload has landed in the DOM yet — the
        // Review Owner row can still show the pre-clear name for a beat after the dialog closes.
        // SharedDocumentDetail.razor only renders the "Review Owner" dt/dd pair at all when
        // ReviewOwnerEmployeeId has a value, so assert the row is gone (auto-retrying) rather than
        // a spinner-clear proxy — same pattern used for EditTitleDescriptionCategoryAsync's h1
        // assertion above.
        await Assertions.Expect(page.Locator("dt:has-text('Review Owner')"))
            .Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Drives the header "Publish" button and its confirmation dialog to completion, then waits
    /// for the page to reload its detail data.
    /// </summary>
    public async Task PublishAsync()
    {
        await PublishHeaderButton.ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await PublishDialog.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // Same stale-overlay race documented on WaitForOverlayToClearAsync above (RequireAcknowledgementAsync
        // already applies it after its own Save) — a caller that immediately drives another click
        // right after Publish (e.g. AuditHistoryDialog_OpensFromDetailPage_AndShowsEntry_AfterPublish's
        // OpenAuditHistoryDialogAsync -> "More actions" button) can otherwise have that click silently
        // eat Playwright's full default action timeout waiting for the fading ".e-dlg-overlay" to stop
        // intercepting pointer events, rather than failing fast or proceeding immediately.
        await WaitForOverlayToClearAsync();

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// Drives the "Acknowledgement" card's "Edit acknowledgement settings" button and
    /// EditSharedCompanyDocumentAcknowledgementDialog.razor to turn on "Requires employee
    /// acknowledgement" with the given due date, then waits for the page to reload its detail
    /// data. A due date is required — PublishSharedCompanyDocumentHandler rejects publishing a
    /// document that requires acknowledgement but has no due date set, so callers needing
    /// RequiresAcknowledgement=true must call this before <see cref="PublishAsync"/>.
    /// </summary>
    public async Task RequireAcknowledgementAsync(DateOnly dueDate)
    {
        // Not Exact — see EditMetadataHeaderButton's remarks: an SfButton's aria-label (if any)
        // overrides its visible text for accessible-name purposes, so Exact matching is fragile
        // here even though this particular button carries no aria-label in source today.
        await AcknowledgementCard.GetByRole(AriaRole.Button, new() { Name = "Edit acknowledgement settings" }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var checkboxWrapper = EditAcknowledgementDialog.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Requires employee acknowledgement" });
        await checkboxWrapper.Locator("label").ClickAsync();

        // Checking this box flips EditSharedCompanyDocumentAcknowledgementDialog.razor's
        // "@if (Model.RequiresAcknowledgement)" block from unmounted to mounted — the date picker,
        // statement field, etc. don't exist in the DOM at all until this Blazor re-render lands,
        // and Syncfusion's SfDatePicker then does its own follow-up JS-interop initialization pass
        // on top of that. Clicking immediately can catch the element mid-(re)creation and get
        // "element was detached from the DOM, retrying" — Playwright's own retry loop usually
        // recovers from a single such hand-off, but back-to-back re-renders (Blazor mount, then
        // Syncfusion's own init) can outlast even its default retry window under load. Wait for
        // the date input to exist and settle before interacting.
        var dateInput = EditAcknowledgementDialog.Locator(".e-date-wrapper input.e-input");
        await dateInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);

        await dateInput.ClickAsync();
        await dateInput.FillAsync(dueDate.ToString("dd/MM/yyyy"));
        await page.Keyboard.PressAsync("Tab");

        await EditAcknowledgementDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await WaitForOverlayToClearAsync();

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    // "Audit History" now lives in the "More actions" overflow menu (BuildMoreActionsItems in
    // SharedDocumentDetail.razor) rather than being its own header button.
    private ILocator AuditHistoryDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Audit History" });
    private ILocator AuditDetailDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Audit Event Detail" });

    /// <summary>Opens the Audit History dialog (SharedCompanyDocumentAuditHistoryDialog.razor) via the "More actions" &gt; "Audit History" menu item.</summary>
    public async Task OpenAuditHistoryDialogAsync()
    {
        await ClickMoreActionsItemAsync("Audit History");
        await AuditHistoryDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // The dialog container mounting doesn't prove its grid has populated yet — Syncfusion
        // populates ".e-row"/".e-rowcell" data on a separate JS tick (same "container before
        // content" race fixed elsewhere in this suite, e.g. EmployeeAdminPage's asset grids). A
        // caller that immediately calls GetAuditHistoryRowCountAsync can otherwise read 0 for a
        // document that genuinely has audit entries.
        await AuditHistoryDialog.Locator(
            "[data-testid='document-audit-history-grid'] .e-row, [data-testid='document-audit-history-grid'] .e-emptyrow")
            .First.WaitForAsync(new() { Timeout = 10_000 });
    }

    public Task<bool> IsAuditHistoryDialogOpenAsync() => AuditHistoryDialog.IsVisibleAsync();

    /// <summary>
    /// Number of rows currently rendered in the Audit History dialog's grid
    /// (data-testid="document-audit-history-grid"), scoped to the dialog so it can't collide with
    /// any other ".e-row" markup elsewhere on the page.
    /// </summary>
    public Task<int> GetAuditHistoryRowCountAsync() =>
        AuditHistoryDialog.Locator("[data-testid='document-audit-history-grid'] .e-row").CountAsync();

    /// <summary>
    /// Clicks the "View" (fa-eye) button on the Audit History grid row whose text contains
    /// <paramref name="rowTextFragment"/> (e.g. an Action value like "Published" or "Acknowledgement Settings Updated"),
    /// opening the Audit Event Detail dialog.
    /// </summary>
    public async Task ClickViewAuditHistoryRowAsync(string rowTextFragment)
    {
        var row = AuditHistoryDialog.Locator("[data-testid='document-audit-history-grid'] .e-row")
            .Filter(new() { HasText = rowTextFragment })
            .First;
        await row.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        await AuditDetailDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsAuditDetailDialogOpenAsync() => AuditDetailDialog.IsVisibleAsync();

    /// <summary>All text content of the Audit Event Detail dialog (Date/User/Action rows plus the Before/After changes table).</summary>
    public async Task<string> GetAuditDetailDialogTextAsync() =>
        (await AuditDetailDialog.InnerTextAsync()).Trim();

    // Scoped to .e-footer-content rather than GetByRole(Button, Name="Close") — the dialog's own
    // ShowCloseIcon header button also carries an accessible name of "Close", so a role/name match
    // resolves to two elements. Mirrors EmployeeEditPage.CloseAuditDetailDialogAsync's established
    // fix for the identical ambiguity on the employee-side audit detail dialog.
    public async Task CloseAuditDetailDialogAsync()
    {
        await AuditDetailDialog.Locator(".e-footer-content button:has-text('Close')").ClickAsync();
        await AuditDetailDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task CloseAuditHistoryDialogAsync()
    {
        await AuditHistoryDialog.Locator(".e-footer-content button:has-text('Close')").ClickAsync();
        await AuditHistoryDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    // ── Edit Acknowledgement Settings dialog (statement field, Reset to Default, publish-lock) ──

    /// <summary>
    /// Opens the "Acknowledgement" card's "Edit acknowledgement settings" dialog without
    /// changing/saving anything — unlike <see cref="RequireAcknowledgementAsync"/>, which drives
    /// the whole toggle-and-save flow, this is for tests that need to inspect or interact with the
    /// dialog's fields directly (e.g. the statement field's locked/editable state, or the "Reset
    /// to Default" button).
    /// </summary>
    public async Task OpenEditAcknowledgementDialogAsync()
    {
        // Not Exact — see EditMetadataHeaderButton's remarks.
        await AcknowledgementCard.GetByRole(AriaRole.Button, new() { Name = "Edit acknowledgement settings" }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    /// <summary>
    /// Syncfusion's modal overlay (".e-dlg-overlay") is a DOM sibling of the dialog itself, not a
    /// descendant, and its close-fade animation can still be intercepting pointer events for a
    /// short moment after the dialog role element itself already reports "Hidden" to Playwright.
    /// A caller that immediately re-clicks the same "Edit" button to reopen the dialog (e.g.
    /// RequireAcknowledgementAsync followed by OpenEditAcknowledgementDialogAsync, or
    /// SaveEditAcknowledgementDialogAsync followed by another OpenEditAcknowledgementDialogAsync)
    /// can otherwise hit "subtree intercepts pointer events" on the stale overlay. Best-effort: if
    /// no overlay is present at all, this is a no-op.
    ///
    /// Also waits for ".e-dlg-container" (the dialog's own outer wrapper, a DIFFERENT element from
    /// ".e-dlg-overlay") to clear — a captured failure (RenamedEditButtons_EachOpenTheirOwnDialog,
    /// closing the "Edit details" dialog via Escape then immediately clicking "Edit audience")
    /// showed the intercepting element was specifically ".e-dlg-container", not the overlay: on
    /// Escape (rather than a Save-triggered close), the container can still be mid-close/lingering
    /// in the DOM briefly after the dialog role itself reports Hidden, independently of whatever
    /// the overlay is doing.
    /// </summary>
    public async Task WaitForOverlayToClearAsync()
    {
        try
        {
            await page.Locator(".e-dlg-overlay").WaitForAsync(
                new() { State = WaitForSelectorState.Detached, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            // Ignore — best-effort settle only.
        }

        try
        {
            await page.Locator(".e-dlg-container").WaitForAsync(
                new() { State = WaitForSelectorState.Detached, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            // Ignore — best-effort settle only.
        }
    }

    public Task<bool> IsEditAcknowledgementDialogOpenAsync() => EditAcknowledgementDialog.IsVisibleAsync();

    // The Acknowledgement Statement field is the only HrTextBox (Multiline, so a <textarea>)
    // rendered inside this dialog.
    private ILocator AcknowledgementStatementTextArea => EditAcknowledgementDialog.Locator("textarea");

    public Task<string> GetAcknowledgementStatementValueAsync() =>
        AcknowledgementStatementTextArea.InputValueAsync();

    public async Task FillAcknowledgementStatementAsync(string value)
    {
        await AcknowledgementStatementTextArea.FillAsync(value);

        // HrTextBox (SfTextBox under the hood) only round-trips its bound value over the Blazor
        // Server circuit on blur/change — same reasoning as CompanyEditPage.TypeIntoTextBoxAsync
        // elsewhere in this suite. A page-level Tab keypress is the wrong way to trigger that blur
        // here, though: this textarea isn't recognized by SfDialog's own focus trap, so Tab can
        // carry focus straight out of the dialog entirely — which SfDialog treats as an
        // outside-focus/close event, firing its own Closed handler
        // (EditSharedCompanyDocumentAcknowledgementDialog's DialogEvents Closed="HandleDialogClosed")
        // and popping the "Unsaved Changes" confirmation dialog on top instead of just committing
        // the value (surfaced as the Save button becoming permanently unclickable/the dialog never
        // closing afterward). Click the Due Date field instead — a genuinely focusable element that
        // stays inside the dialog's own DOM subtree, so it blurs the textarea without ever handing
        // focus outside the dialog. Only rendered alongside the statement field, gated by the same
        // Model.RequiresAcknowledgement condition, so it's always present when this method is.
        var dueDateInput = EditAcknowledgementDialog.Locator(".e-date-wrapper input.e-input");
        await dueDateInput.ClickAsync();

        // That commit also causes Syncfusion to destroy and recreate the textarea's own DOM node
        // (rather than patch it in place), so polling a *freshly re-resolved* locator each time —
        // rather than holding a single ElementHandle across the round-trip — avoids racing that
        // teardown. Same class of fix as DataImportWizardPage.GetMappingSelectionAsync's poll.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await AcknowledgementStatementTextArea.InputValueAsync() == value)
                return;

            await page.WaitForTimeoutAsync(250);
        }
    }

    /// <summary>
    /// True once the document's Status is no longer "Draft" — EditSharedCompanyDocumentAcknowledgementDialog.razor
    /// sets HrTextBox's Readonly to this, rendering a "readonly" attribute on the underlying textarea.
    /// </summary>
    public async Task<bool> IsAcknowledgementStatementReadOnlyAsync() =>
        await AcknowledgementStatementTextArea.GetAttributeAsync("readonly") is not null;

    private ILocator ResetAcknowledgementStatementButton =>
        EditAcknowledgementDialog.GetByRole(AriaRole.Button, new() { Name = "Reset to Default" });

    public Task<bool> IsResetAcknowledgementStatementButtonDisabledAsync() =>
        ResetAcknowledgementStatementButton.IsDisabledAsync();

    /// <summary>
    /// Clicks "Reset to Default" in the Edit Acknowledgement Settings dialog. Immediately after
    /// the dialog opens, Syncfusion is still settling its own re-render of the dialog body (same
    /// destroy/recreate-on-bind churn documented on FillAcknowledgementStatementAsync above), which
    /// can detach the button out from under a single ClickAsync's actionability wait — surfaced as
    /// "element is not stable"/"was detached from the DOM, retrying" — Playwright's own built-in
    /// retry only covers the current element handle, not the button being torn down and rebuilt.
    /// Re-resolving the locator per attempt and retrying the click itself rides out that churn.
    /// </summary>
    public async Task ClickResetAcknowledgementStatementToDefaultAsync()
    {
        var beforeValue = await AcknowledgementStatementTextArea.InputValueAsync();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            try
            {
                await ResetAcknowledgementStatementButton.ClickAsync(new() { Timeout = 5_000 });
                break;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await page.WaitForTimeoutAsync(250);
            }
        }

        // ResetToDefault() is a server-side C# method — the click only queues its Blazor Server
        // round-trip; the textarea's value doesn't reflect the reset until that round-trip lands
        // and re-renders. A caller reading the value immediately after this returns can otherwise
        // still see the pre-reset value.
        var valueDeadline = DateTime.UtcNow.AddSeconds(10);
        while (await AcknowledgementStatementTextArea.InputValueAsync() == beforeValue && DateTime.UtcNow < valueDeadline)
            await page.WaitForTimeoutAsync(100);
    }

    /// <summary>
    /// Whether the "Locked after publishing — upload a new version to change the wording." note is
    /// shown, which EditSharedCompanyDocumentAcknowledgementDialog.razor only renders once the
    /// statement field is locked (Status != "Draft").
    /// </summary>
    public Task<bool> IsAcknowledgementLockedNoteVisibleAsync() =>
        EditAcknowledgementDialog.GetByText("Locked after publishing").IsVisibleAsync();

    /// <summary>Saves the Edit Acknowledgement Settings dialog, then waits for it to close and the page to reload its detail data.</summary>
    public async Task SaveEditAcknowledgementDialogAsync()
    {
        await EditAcknowledgementDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await WaitForOverlayToClearAsync();

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Opens the Publish confirmation dialog via the header "Publish" button, without confirming or cancelling it.</summary>
    public async Task OpenPublishDialogAsync()
    {
        await PublishHeaderButton.ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsPublishDialogOpenAsync() => PublishDialog.IsVisibleAsync();

    public async Task ClickPublishCancelAsync()
    {
        await PublishDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>Opens the Archive confirmation dialog via "More actions" &gt; "Archive".</summary>
    public async Task OpenArchiveDialogAsync()
    {
        await ClickMoreActionsItemAsync("Archive");
        await ArchiveDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsArchiveDialogOpenAsync() => ArchiveDialog.IsVisibleAsync();

    public async Task FillArchiveReasonAsync(string reason)
    {
        await page.Locator("#archive-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Clicks the dialog's own "Archive"/"Archiving…" footer button (does not wait for the dialog to close, since an empty reason keeps it open).</summary>
    public Task ClickArchiveConfirmAsync() =>
        ArchiveDialog.GetByRole(AriaRole.Button, new() { Name = "Archive", Exact = true }).ClickAsync();

    public Task ClickArchiveCancelAsync() =>
        ArchiveDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

    /// <summary>The inline validation/error text shown inside the Archive dialog (e.g. "A reason is required."), or null if none is shown.</summary>
    public async Task<string?> GetArchiveErrorAsync()
    {
        var error = ArchiveDialog.Locator(".alert-danger");
        // A bare instant IsVisibleAsync() right after ClickArchiveConfirmAsync() can race the
        // client-side validation render (same "read before it settles" class of race fixed
        // elsewhere in this suite) and return null before the message has actually appeared. Give
        // it a bounded wait instead of failing fast.
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Fills in the reason and confirms, then waits for the dialog to close and the page to
    /// reload its detail data. Assumes the reason is non-blank (a blank reason keeps the dialog
    /// open — use OpenArchiveDialogAsync/ClickArchiveConfirmAsync/GetArchiveErrorAsync directly
    /// to exercise that validation path).
    /// </summary>
    public async Task ArchiveAsync(string reason)
    {
        await OpenArchiveDialogAsync();
        await FillArchiveReasonAsync(reason);
        await ClickArchiveConfirmAsync();
        await WaitForArchiveDialogToCloseAsync();
    }

    /// <summary>
    /// Waits for a successful archive to close the dialog and the page to finish reloading its
    /// detail data. Only resolves once the reason was accepted — a validation failure leaves the
    /// dialog open, so callers exercising that path should assert on GetArchiveErrorAsync/
    /// IsArchiveDialogOpenAsync instead of calling this.
    /// </summary>
    public async Task WaitForArchiveDialogToCloseAsync()
    {
        await ArchiveDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Opens the "Mark Document as Expired" confirmation dialog via "More actions" &gt; "Mark Expired".</summary>
    public async Task OpenExpireDialogAsync()
    {
        await ClickMoreActionsItemAsync("Mark Expired");
        await ExpireDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsExpireDialogOpenAsync() => ExpireDialog.IsVisibleAsync();

    /// <summary>The Expire dialog's body paragraph text (e.g. confirming the document title and consequences of expiring it).</summary>
    public async Task<string> GetExpireDialogBodyTextAsync() =>
        (await ExpireDialog.Locator("p").First.InnerTextAsync()).Trim();

    /// <summary>Clicks the dialog's own "Mark Expired"/"Marking Expired…" footer button (does not wait for the dialog to close).</summary>
    public Task ClickExpireConfirmAsync() =>
        ExpireDialog.GetByRole(AriaRole.Button, new() { Name = "Mark Expired", Exact = true }).ClickAsync();

    public async Task ClickExpireCancelAsync()
    {
        await ExpireDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await ExpireDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        // Same stale-overlay race documented on WaitForOverlayToClearAsync above: the dialog role
        // element reporting "Hidden" doesn't guarantee its ".e-dlg-overlay" fade-out has finished
        // intercepting pointer events yet. A caller that immediately clicks "More actions" right
        // after Cancel (e.g. MarkExpired_OnPublishedDocument_OpensDialogWithWording_CancelLeavesUnchanged's
        // IsExpireButtonVisibleAsync check) can otherwise silently eat Playwright's full default
        // action timeout waiting for that stale overlay to stop intercepting the click.
        await WaitForOverlayToClearAsync();
    }

    /// <summary>The inline error text shown inside the Expire dialog when the server rejects the request (e.g. already Expired/Archived), or null if none is shown.</summary>
    public async Task<string?> GetExpireErrorAsync()
    {
        var error = ExpireDialog.Locator(".alert-danger");
        // Same "read before it settles" race as GetArchiveErrorAsync/GetReviewValidationErrorAsync
        // above — a bare instant IsVisibleAsync() right after confirming can race the error
        // banner's render and return null before it has actually appeared.
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Drives the header "Mark Expired" button and its confirmation dialog to completion, then
    /// waits for the dialog to close and the page to reload its detail data.
    /// </summary>
    public async Task ExpireAsync()
    {
        await OpenExpireDialogAsync();
        await ClickExpireConfirmAsync();
        await WaitForExpireDialogToCloseAsync();
    }

    /// <summary>
    /// Waits for a successful expire to close the dialog and the page to finish reloading its
    /// detail data. Only resolves once the request succeeded — a server-side rejection leaves the
    /// dialog open, so callers exercising that path should assert on GetExpireErrorAsync/
    /// IsExpireDialogOpenAsync instead of calling this.
    /// </summary>
    public async Task WaitForExpireDialogToCloseAsync()
    {
        await ExpireDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Opens the Complete Review dialog (CompleteSharedCompanyDocumentReviewDialog.razor) via the header "Review Document" button, without confirming or cancelling it.</summary>
    public async Task OpenReviewDialogAsync()
    {
        await ReviewHeaderButton.ClickAsync();
        await ReviewDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsReviewDialogOpenAsync() => ReviewDialog.IsVisibleAsync();

    /// <summary>
    /// Text of a labelled dt/dd metadata row inside the Complete Review dialog (e.g. "Title",
    /// "Category", "Next Review Date", "Review Frequency", "Review Owner"), or null when the row
    /// isn't rendered (Review Frequency and Review Owner are only rendered when set — same as the
    /// page's own Document Metadata card). Deliberately scoped to the dialog itself rather than a
    /// bare "dt:has-text(...) + dd" page-level locator, since SharedDocumentDetail.razor's own
    /// Document Metadata card renders dt/dd rows with these exact same labels underneath the
    /// dialog, which would otherwise resolve to two elements.
    /// </summary>
    public async Task<string?> GetReviewDialogMetadataRowAsync(string label)
    {
        var row = ReviewDialog.Locator($"dt:has-text('{label}') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    public async Task FillReviewNotesAsync(string notes)
    {
        await page.Locator("#review-notes").FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Clicks the dialog's own "Complete Review"/"Saving…" footer button (does not wait for the dialog to close, since blank/whitespace notes keeps it open).</summary>
    public Task ClickReviewConfirmAsync() =>
        ReviewDialog.GetByRole(AriaRole.Button, new() { Name = "Complete Review", Exact = true }).ClickAsync();

    public async Task ClickReviewCancelAsync()
    {
        await ReviewDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await ReviewDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>The inline validation message shown inside the Complete Review dialog when notes are blank/whitespace (e.g. "Review notes are required."), or null if none is shown.</summary>
    public async Task<string?> GetReviewValidationErrorAsync()
    {
        var error = ReviewDialog.Locator(".text-danger.small.mt-1");
        // Same "read before it settles" race as GetArchiveErrorAsync above — a bare instant
        // IsVisibleAsync() right after ClickReviewConfirmAsync() can race the client-side
        // validation render and return null before the message has actually appeared.
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Fills in the review notes and confirms, then waits for the dialog to close and the page to
    /// reload its detail data. Assumes the notes are non-blank (blank/whitespace notes keep the
    /// dialog open — use OpenReviewDialogAsync/ClickReviewConfirmAsync/GetReviewValidationErrorAsync
    /// directly to exercise that validation path).
    /// </summary>
    public async Task CompleteReviewAsync(string notes)
    {
        await OpenReviewDialogAsync();
        await FillReviewNotesAsync(notes);
        await ClickReviewConfirmAsync();
        await WaitForReviewDialogToCloseAsync();
    }

    /// <summary>
    /// Waits for a successful review completion to close the dialog and the page to finish
    /// reloading its detail data. Only resolves once the notes were accepted — a validation
    /// failure leaves the dialog open, so callers exercising that path should assert on
    /// GetReviewValidationErrorAsync/IsReviewDialogOpenAsync instead of calling this.
    /// </summary>
    public async Task WaitForReviewDialogToCloseAsync()
    {
        await ReviewDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// Sets a file on the Complete Review dialog's optional "Renewed File" input
    /// (CompleteSharedCompanyDocumentReviewDialog.razor). Attaching a file here (in addition to
    /// the required review notes) drives the dialog to also call
    /// DocumentService.UploadSharedCompanyDocumentVersionAsync before completing the review,
    /// adding a new row to the Version History grid — see SharedDocumentReviewRenewalTests.
    /// </summary>
    public Task SetReviewRenewedFileAsync(string filePath) =>
        ReviewDialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

    // Scoped to the Complete Review dialog so this doesn't collide with the same-labelled
    // checkbox rendered by UploadSharedCompanyDocumentVersionDialog.razor's own "Upload New
    // Version" dialog (both use the identical SfCheckBox label).
    private ILocator ReviewReacknowledgementCheckbox => ReviewDialog.Locator(".e-checkbox-wrapper")
        .Filter(new() { HasText = "Requires employees to acknowledge this version again" });

    /// <summary>
    /// Whether the "Requires employees to acknowledge this version again" checkbox is currently
    /// rendered inside the Complete Review dialog. CompleteSharedCompanyDocumentReviewDialog.razor
    /// only renders it once the document's RequiresAcknowledgement is true AND a "Renewed File"
    /// has been selected via <see cref="SetReviewRenewedFileAsync"/> — see
    /// SharedDocumentReviewRenewalTests.
    /// </summary>
    public Task<bool> IsReviewReacknowledgementCheckboxVisibleAsync() =>
        ReviewReacknowledgementCheckbox.WaitUntilVisibleAsync();

    /// <summary>
    /// Checks the Complete Review dialog's "Requires employees to acknowledge this version
    /// again" checkbox. Assumes it's currently rendered — i.e. a file has already been selected
    /// via <see cref="SetReviewRenewedFileAsync"/> on a document with RequiresAcknowledgement true.
    /// </summary>
    public Task CheckReviewReacknowledgementAsync() => ReviewReacknowledgementCheckbox.Locator("label").ClickAsync();

    /// <summary>
    /// Fills in the review notes, attaches a "Renewed File", optionally checks the
    /// reacknowledgement checkbox, and confirms — driving the same notes-then-upload-then-
    /// complete-review flow CompleteSharedCompanyDocumentReviewDialog.razor's ConfirmAsync
    /// performs once a file is selected — then waits for the dialog to close and the page to
    /// reload its detail data (including the Version History grid). Assumes the notes are
    /// non-blank and the file is a valid upload (see <see cref="CompleteReviewAsync"/> for the
    /// notes-only, validation-focused equivalent).
    /// </summary>
    public async Task CompleteReviewWithRenewedFileAsync(string notes, string filePath, bool requiresReacknowledgement = false)
    {
        await OpenReviewDialogAsync();
        await FillReviewNotesAsync(notes);
        await SetReviewRenewedFileAsync(filePath);

        if (requiresReacknowledgement)
        {
            await CheckReviewReacknowledgementAsync();
        }

        await ClickReviewConfirmAsync();
        await WaitForReviewDialogToCloseAsync();
    }

    /// <summary>Opens the Audience-edit dialog (EditSharedCompanyDocumentAudienceDialog.razor) via the "Audience" overview-card's "Edit audience" button.</summary>
    public async Task OpenEditAudienceDialogAsync()
    {
        // Not Exact — see EditMetadataHeaderButton's remarks.
        await AudienceCard.GetByRole(AriaRole.Button, new() { Name = "Edit audience" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Document Audience" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    /// <summary>
    /// Text of the "Audience" overview-card's summary paragraph (e.g. "All Employees" or
    /// "Departments: Engineering") — a plain &lt;p class="doc-detail-audience-summary"&gt; now,
    /// not a dt/dd pair.
    /// </summary>
    public async Task<string> GetAudienceSummaryAsync() =>
        (await page.Locator(".doc-detail-audience-summary").InnerTextAsync()).Trim();

    /// <summary>
    /// Whether the "Not yet published — employees can't see this document" warning badge is shown
    /// on the "Audience" overview-card — SharedDocumentDetail.razor only renders it while the
    /// document's Status is "Draft".
    /// </summary>
    public Task<bool> IsDraftAudienceWarningVisibleAsync() =>
        page.Locator(".doc-detail-note-badge").IsVisibleAsync();

    /// <summary>The "Created by / Last updated by / Published by / Archived by" summary line at the bottom of the page.</summary>
    public ILocator FooterSummary => page.Locator("p.text-muted.small.mb-0");

    public async Task<string> GetFooterSummaryTextAsync() =>
        (await FooterSummary.InnerTextAsync()).Trim();

    // The Version History and Review History grids now share a single SfTab
    // (".doc-detail-history-card") rather than each having its own ".overview-card" — the old
    // "Filter(HasText: 'Version History')" scoping doesn't distinguish them any more, since both
    // tab headers' text lives inside that one shared card. Syncfusion's SfTab keeps both content
    // panes (".e-content > .e-item") in the DOM and toggles their visibility rather than
    // mounting/unmounting them, so every grid locator below is instead scoped to whichever pane is
    // currently *visible* (Playwright's ":visible" pseudo-class, already used elsewhere in this
    // file for the save-conflict banner) after explicitly selecting the relevant tab — this avoids
    // the exact "grids silently share rows" collision the old scoping was written to prevent (see
    // git history) via a different mechanism.
    private ILocator VersionHistoryTab => page.GetByRole(AriaRole.Tab, new() { Name = "Version History" });
    private ILocator ReviewHistoryTab => page.GetByRole(AriaRole.Tab, new() { Name = "Review History" });
    private ILocator ActiveHistoryTabPane => page.Locator(".doc-detail-history-card .e-content > .e-item:visible").First;

    /// <summary>Selects the "Version History" tab (a no-op click if it's already active, since it's the default) and returns its content pane.</summary>
    private async Task<ILocator> SelectVersionHistoryTabAsync()
    {
        await VersionHistoryTab.ClickAsync();
        await Assertions.Expect(VersionHistoryTab).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
        return ActiveHistoryTabPane;
    }

    /// <summary>Selects the "Review History" tab and returns its content pane.</summary>
    private async Task<ILocator> SelectReviewHistoryTabAsync()
    {
        await ReviewHistoryTab.ClickAsync();
        await Assertions.Expect(ReviewHistoryTab).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
        return ActiveHistoryTabPane;
    }

    public async Task<int> GetVersionRowCountAsync()
    {
        var pane = await SelectVersionHistoryTabAsync();
        return await pane.Locator(".e-row").CountAsync();
    }

    /// <summary>
    /// Waits for the Version History grid to show exactly <paramref name="expectedCount"/> rows,
    /// then returns that count. Unlike <see cref="GetVersionRowCountAsync"/> (a one-shot,
    /// non-retrying <c>CountAsync()</c> snapshot), this polls via Playwright's own
    /// <c>ToHaveCountAsync</c> assertion — necessary because neither
    /// <see cref="UploadNewVersionAsync"/>'s "dialog hidden" nor its "spinner gone" wait guarantees
    /// the Blazor Server render carrying the new row has actually been flushed over SignalR and
    /// painted into the DOM by the time control returns to the caller; an immediate one-shot count
    /// can catch the grid mid-update and read one row short.
    /// </summary>
    public async Task<int> WaitForVersionRowCountAsync(int expectedCount)
    {
        var pane = await SelectVersionHistoryTabAsync();
        var rows = pane.Locator(".e-row");
        await Assertions.Expect(rows).ToHaveCountAsync(expectedCount, new() { Timeout = 15_000 });
        return await rows.CountAsync();
    }

    /// <summary>Header text of every column currently rendered on the Version History grid.</summary>
    public async Task<IReadOnlyList<string>> GetVersionColumnHeadersAsync()
    {
        var pane = await SelectVersionHistoryTabAsync();
        // See GetReviewHistoryColumnHeadersAsync — wait for the grid to render before reading headers.
        await pane.Locator(".e-row, .e-emptyrow, .doc-detail-empty-state").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var headers = await pane.Locator(".e-grid .e-headercell").AllInnerTextsAsync();
        return headers.Select(h => h.Trim()).ToList();
    }

    /// <summary>
    /// Returns the text of the given 0-based column index for the Version History grid row whose
    /// text contains <paramref name="rowTextFragment"/> (e.g. the version's uploaded file name,
    /// which is unique per version in these tests). Column order matches the compact set rendered
    /// by SharedDocumentDetail.razor's Version History GridColumns: 0=Version, 1=Publication
    /// Status, 2=File Name, 3=Uploaded, 4=Uploaded By, 5=Details (icon button — see
    /// <see cref="GetVersionDetailAsync"/> for Note/Required Ack/Effective Date, which moved into
    /// a per-row popup), 6=Download.
    /// </summary>
    public async Task<string> GetVersionRowCellAsync(string rowTextFragment, int columnIndex)
    {
        var pane = await SelectVersionHistoryTabAsync();
        var row = pane.Locator(".e-row").Filter(new() { HasText = rowTextFragment }).First;
        return (await row.Locator(".e-rowcell").Nth(columnIndex).InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Opens the per-row "Details" popup (fa-circle-info icon button, aria-label "Show details for
    /// version N") for the Version History row whose text contains <paramref name="rowTextFragment"/>,
    /// reads its Note / Required Ack / Effective Date values, then closes the popup again. These
    /// three fields moved out of the grid's own columns and into this popup as part of the grid's
    /// compaction — see SharedDocumentDetail.razor's OpenVersionDetail/_versionDetailOpen.
    /// </summary>
    public async Task<(string Note, string RequiredAck, string EffectiveDate)> GetVersionDetailAsync(string rowTextFragment)
    {
        var pane = await SelectVersionHistoryTabAsync();
        var row = pane.Locator(".e-row").Filter(new() { HasText = rowTextFragment }).First;
        await row.Locator("button[aria-label^='Show details for version']").ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { NameRegex = new Regex("details", RegexOptions.IgnoreCase) });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var note = (await dialog.Locator("dt:has-text('Note') + dd").InnerTextAsync()).Trim();
        var requiredAck = (await dialog.Locator("dt:has-text('Required Ack') + dd").InnerTextAsync()).Trim();
        var effectiveDate = (await dialog.Locator("dt:has-text('Effective Date') + dd").InnerTextAsync()).Trim();

        // ShowCloseIcon="true" on this dialog means its header close ("X") button also carries an
        // accessible name of "Close", colliding with the footer's own "Close" button under a bare
        // role/name match — scope to the footer specifically, same fix already applied to
        // CloseAuditDetailDialogAsync/CloseAuditHistoryDialogAsync above.
        await dialog.Locator(".e-footer-content button:has-text('Close')").ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        return (note, requiredAck, effectiveDate);
    }

    /// <summary>
    /// The href of the per-version Download link (fa-download icon, title="Download this
    /// version") in the row whose text contains <paramref name="rowTextFragment"/>. Points at
    /// api/companies/{companyId}/shared-documents/{documentId}/versions/{versionNumber}/download
    /// — a relative &lt;a&gt; the browser follows directly, so no live download needs to occur
    /// for this to be asserted against.
    /// </summary>
    public async Task<string?> GetVersionDownloadHrefAsync(string rowTextFragment)
    {
        var pane = await SelectVersionHistoryTabAsync();
        var row = pane.Locator(".e-row").Filter(new() { HasText = rowTextFragment }).First;
        return await row.Locator("a[title='Download this version']").GetAttributeAsync("href");
    }

    /// <summary>
    /// Drives the "Upload New Version" button and its dialog
    /// (UploadSharedCompanyDocumentVersionDialog.razor) to completion, then waits for the page's
    /// Version History grid to refresh with the new version.
    /// </summary>
    public async Task UploadNewVersionAsync(string versionNote, string filePath)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload New Version" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Upload New Version" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("What changed in this version?").FillAsync(versionNote);
        await page.Keyboard.PressAsync("Tab");
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// True once the "Review History" tab is selectable/present — the tab itself (unlike the old
    /// standalone card) is always rendered regardless of whether the document has ever been
    /// reviewed, so this is really just a presence check on the tab header.
    /// </summary>
    public Task<bool> IsReviewHistoryCardVisibleAsync() => ReviewHistoryTab.IsVisibleAsync();

    /// <summary>
    /// Selects the "Review History" tab and waits for its grid to finish its own JS render tick (a
    /// data row or its ".e-emptyrow" empty-state sibling present — same "'.e-grid' alone doesn't
    /// prove rows are queryable" reasoning as e.g. CandidateListPage.RowsRenderedSelector), then
    /// returns the number of actual data rows (0 for the empty-state case, since ".e-emptyrow"
    /// itself is excluded from this count).
    /// </summary>
    public async Task<int> GetReviewHistoryRowCountAsync()
    {
        var pane = await SelectReviewHistoryTabAsync();
        await pane.Locator(".e-row, .e-emptyrow").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return await pane.Locator(".e-row").CountAsync();
    }

    /// <summary>
    /// Waits for the Review History grid to show exactly <paramref name="expectedCount"/> rows,
    /// then returns that count. Unlike <see cref="GetReviewHistoryRowCountAsync"/> (a one-shot
    /// snapshot), this polls via Playwright's own <c>ToHaveCountAsync</c> assertion — necessary
    /// because neither <see cref="CompleteReviewAsync"/>'s "dialog hidden" nor its "spinner gone"
    /// wait guarantees the Blazor Server render carrying the new history row has actually been
    /// flushed over SignalR and painted into the DOM by the time control returns to the caller
    /// (same reasoning as <see cref="WaitForVersionRowCountAsync"/>).
    /// </summary>
    public async Task<int> WaitForReviewHistoryRowCountAsync(int expectedCount)
    {
        var pane = await SelectReviewHistoryTabAsync();
        var rows = pane.Locator(".e-row");
        await Assertions.Expect(rows).ToHaveCountAsync(expectedCount, new() { Timeout = 15_000 });
        return await rows.CountAsync();
    }

    /// <summary>Header text of every column currently rendered on the Review History grid.</summary>
    public async Task<IReadOnlyList<string>> GetReviewHistoryColumnHeadersAsync()
    {
        var pane = await SelectReviewHistoryTabAsync();
        // The tab reporting aria-selected only proves the tab switched — the pane's Syncfusion grid
        // (headers included) renders on a later pass, so an instant AllInnerTextsAsync() snapshot
        // could read no/partial headers. Wait for the grid body (a row or the empty row) first.
        await pane.Locator(".e-row, .e-emptyrow").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var headers = await pane.Locator(".e-headercell").AllInnerTextsAsync();
        return headers.Select(h => h.Trim()).ToList();
    }

    /// <summary>
    /// Returns the text of the given 0-based column index for the Review History grid row at the
    /// given 0-based <paramref name="rowIndex"/> (0 = topmost row — the grid is sorted
    /// newest-first server-side by ReviewDate, with no client-side sorting on top of that). Column
    /// order matches SharedDocumentDetail.razor's Review History GridColumns: 0=Review Date,
    /// 1=Reviewer, 2=Notes, 3=Previous Review Date.
    /// </summary>
    public async Task<string> GetReviewHistoryRowCellAsync(int rowIndex, int columnIndex)
    {
        var pane = await SelectReviewHistoryTabAsync();
        var row = pane.Locator(".e-row").Nth(rowIndex);
        return (await row.Locator(".e-rowcell").Nth(columnIndex).InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Count of interactive controls (buttons, links, or icon glyphs) rendered anywhere inside the
    /// Review History grid's rows — expected to always be 0, since the grid is strictly read-only
    /// (no edit/delete/action column), unlike the Version History grid (which has per-row Download
    /// and Details controls). Deliberately scoped to just the grid's own rows (".e-row" inside the
    /// active pane), not the pane's "Record review" button that sits above the grid.
    /// </summary>
    public async Task<int> GetReviewHistoryRowActionControlCountAsync()
    {
        var pane = await SelectReviewHistoryTabAsync();
        return await pane.Locator(".e-row button, .e-row a, .e-row i").CountAsync();
    }

    // ── Ticket 2: optimistic-concurrency conflict on the metadata / audience / acknowledgement edit dialogs ──
    // Each of the three edit dialogs (EditSharedCompanyDocument{Metadata,Audience,Acknowledgement}Dialog.razor)
    // renders the shared <SaveConflictBanner> — a `div.alert.alert-warning.save-conflict-banner[role='alert']`
    // with a "Reload latest values" Syncfusion button — when its save is rejected with HTTP 409
    // (code=="concurrency"). The banner is matched by the component's own `.save-conflict-banner`
    // class scoped to the owning dialog, additionally filtered on the "Reload latest values" action
    // so an unrelated warning alert can never satisfy strict mode. Match on structure, not text.

    private ILocator ConflictBannerIn(ILocator dialog) =>
        // Only one edit dialog is ever open at a time, and <SaveConflictBanner> is the sole
        // `.save-conflict-banner` on the page — match it directly rather than through the dialog's
        // computed accessible name + a nested Has-button filter (that chain was resolving to zero
        // even with the banner visibly rendered). Scope to :visible so the collapsed (Visible=false)
        // instance in a just-closed dialog can't satisfy it.
        page.Locator(".save-conflict-banner:visible");

    private async Task SaveDialogExpectingConflictAsync(ILocator dialog)
    {
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await ConflictBannerIn(dialog).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    private async Task SaveDialogExpectingSuccessAsync(ILocator dialog)
    {
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    private async Task ClickDialogReloadLatestAsync(ILocator dialog)
    {
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConflictBannerIn(dialog).WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    // ── Metadata dialog ──────────────────────────────────────────────────────

    /// <summary>
    /// Opens the metadata edit dialog via the page header's "Edit" button and waits for its async
    /// data load to settle. SaveCoreAsync dereferences Model.CategoryId.Value — which
    /// OnOpenedAsync prefills asynchronously from the loaded document — so this also waits for the
    /// Category combobox to show a value before any Save is attempted.
    /// </summary>
    public async Task OpenMetadataDialogAsync()
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await Assertions.Expect(
                EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Category" })
                    .Locator(".e-input-group input").First)
            .Not.ToHaveValueAsync("", new() { Timeout = 15_000 });
    }

    public Task SetMetadataTitleAsync(string value) =>
        ClearAndTypeAsync(EditMetadataDialog.GetByPlaceholder("Document title"), value);

    public Task<string> GetMetadataTitleValueAsync() =>
        EditMetadataDialog.GetByPlaceholder("Document title").InputValueAsync();

    /// <summary>Auto-retrying wait for the metadata dialog's Title field to hold <paramref name="expected"/> (used after a reload round-trip).</summary>
    public async Task WaitForMetadataTitleValueAsync(string expected) =>
        await Assertions.Expect(EditMetadataDialog.GetByPlaceholder("Document title"))
            .ToHaveValueAsync(expected, new() { Timeout = 15_000 });

    public Task<bool> IsMetadataDialogOpenAsync() => EditMetadataDialog.IsVisibleAsync();

    /// <summary>
    /// Closes the Edit Document Metadata dialog via its header "X" (ShowCloseIcon), NOT the
    /// Escape key. A captured failure (RenamedEditButtons_EachOpenTheirOwnDialog) showed Escape
    /// unreliably closing this specific dialog — this dialog has a nested SfDatePicker (Effective
    /// Date), and if focus is on/near it, Escape is plausibly consumed by the date-picker's own
    /// popup handling rather than propagating up to the SfDialog, leaving the dialog and its
    /// ".e-dlg-container" genuinely still mounted (not just mid-close-animation) — no amount of
    /// waiting after the keypress fixes that, since the dialog was never actually told to close.
    /// The header close button is a real, deterministic click target unaffected by focus location.
    /// </summary>
    public async Task CloseMetadataDialogAsync()
    {
        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await WaitForOverlayToClearAsync();
    }

    public Task<bool> IsMetadataConflictBannerVisibleAsync() => ConflictBannerIn(EditMetadataDialog).IsVisibleAsync();
    public Task SaveMetadataDialogExpectingConflictAsync() => SaveDialogExpectingConflictAsync(EditMetadataDialog);
    public Task SaveMetadataDialogExpectingSuccessAsync() => SaveDialogExpectingSuccessAsync(EditMetadataDialog);

    public async Task ClickMetadataReloadLatestAsync()
    {
        await ClickDialogReloadLatestAsync(EditMetadataDialog);
        // ReloadLatestAsync re-fetches the document and re-runs OnOpenedAsync (category + employee
        // loads) before re-binding the form; give the re-bound values a beat to land.
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>Opens the metadata dialog, replaces the Title, and saves successfully — used by a second tab to bump the document's version.</summary>
    public async Task ChangeMetadataTitleAsync(string newTitle)
    {
        await OpenMetadataDialogAsync();
        await SetMetadataTitleAsync(newTitle);
        await SaveMetadataDialogExpectingSuccessAsync();
        await Assertions.Expect(page.Locator("h1").First).ToHaveTextAsync(newTitle, new() { Timeout = 15_000 });
    }

    // ── Audience dialog ──────────────────────────────────────────────────────

    private ILocator EditAudienceDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Document Audience" });

    /// <summary>Opens the Audience edit dialog and waits for its SfMultiSelect fields (gated on an async data load) to mount.</summary>
    public async Task OpenAudienceDialogAsync()
    {
        await OpenEditAudienceDialogAsync();
        await EditAudienceDialog.GetByPlaceholder("Any department")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public Task<bool> IsAudienceDialogOpenAsync() => EditAudienceDialog.IsVisibleAsync();

    /// <summary>Closes the Edit Document Audience dialog via its header "X" — see CloseMetadataDialogAsync's remarks on why Escape is unreliable for these SfDialogs.</summary>
    public async Task CloseAudienceDialogAsync()
    {
        await EditAudienceDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await EditAudienceDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await WaitForOverlayToClearAsync();
    }

    public Task<bool> IsAudienceConflictBannerVisibleAsync() => ConflictBannerIn(EditAudienceDialog).IsVisibleAsync();
    public Task SaveAudienceDialogExpectingConflictAsync() => SaveDialogExpectingConflictAsync(EditAudienceDialog);
    public Task SaveAudienceDialogExpectingSuccessAsync() => SaveDialogExpectingSuccessAsync(EditAudienceDialog);

    public async Task ClickAudienceReloadLatestAsync()
    {
        await ClickDialogReloadLatestAsync(EditAudienceDialog);
        await EditAudienceDialog.GetByPlaceholder("Any department")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.WaitForTimeoutAsync(300);
    }

    // ── Acknowledgement dialog ───────────────────────────────────────────────

    public Task<bool> IsAcknowledgementDialogOpenAsync() => EditAcknowledgementDialog.IsVisibleAsync();
    public Task<bool> IsAcknowledgementConflictBannerVisibleAsync() => ConflictBannerIn(EditAcknowledgementDialog).IsVisibleAsync();
    public Task SaveAcknowledgementDialogExpectingConflictAsync() => SaveDialogExpectingConflictAsync(EditAcknowledgementDialog);
    public Task SaveAcknowledgementDialogExpectingSuccessAsync() => SaveDialogExpectingSuccessAsync(EditAcknowledgementDialog);

    public async Task ClickAcknowledgementReloadLatestAsync()
    {
        await ClickDialogReloadLatestAsync(EditAcknowledgementDialog);
        await page.WaitForTimeoutAsync(300);
    }
}
