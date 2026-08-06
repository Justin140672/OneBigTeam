using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyEmployee;

internal sealed class Endpoint(GetMyEmployeeHandler handler, ICurrentUser currentUser) : EndpointWithoutRequest<GetMyEmployeeResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, which only matches
        // Employee.Id (and this app's UserId convention generally) for real production users who
        // have no UserProfile row yet (see SupabaseCurrentUserResolutionMiddleware's fallback).
        // Every dev persona (and every real user post-signup) has a UserProfile row, so
        // ICurrentUser.UserId correctly resolves to profile.Id instead — the id Employee.Id/seed
        // data and every other endpoint in this app actually key off.
        var userId = currentUser.UserId;
        if (userId is null)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(Route<string>("companyId"), out var companyId))
        {
            await Send.ResultAsync(TypedResults.BadRequest());
            return;
        }

        var result = await handler.HandleAsync(companyId, userId.Value, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
