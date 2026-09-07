namespace HR.Modules.Marketing.Features.GetMarketingContent;

// This endpoint takes no input (it returns the whole published content set). FastEndpoints'
// RequestBinder requires at least one publicly accessible member on the request DTO, so this
// positional record carries a single unused parameter purely to satisfy that constraint.
internal sealed record GetMarketingContentRequest(string? Unused = null);
