namespace HR.Modules.Marketing.Features.ListMarketingContent;

// Returns the full content set (published and unpublished). Takes no input; the single unused
// positional parameter exists only to satisfy the FastEndpoints request binder.
internal sealed record ListMarketingContentRequest(string? Unused = null);
