using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HR.SharedKernel.Tests;

// Ticket 2 (optimistic concurrency) base code: the single Error -> IResult translator every
// opted-in FastEndpoints endpoint now delegates to. Pins the status-code mapping and the
// { error, code } body shape so a regression here is caught without a full endpoint spin-up.
//
// Conflict<T>/NotFound<T>/BadRequest<T> are closed over an anonymous type, so the tests assert
// against the non-generic IStatusCodeHttpResult / IValueHttpResult interfaces rather than the
// closed generic types (generic result types are invariant and not assignable to <object>).
public class ProblemResultsTests
{
    private static int StatusOf(IResult result)
        => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode!.Value;

    private static (string? Error, string? Code) BodyOf(IResult result)
    {
        var body = Assert.IsAssignableFrom<IValueHttpResult>(result).Value!;
        var type = body.GetType();
        return ((string?)type.GetProperty("error")!.GetValue(body),
                (string?)type.GetProperty("code")!.GetValue(body));
    }

    [Fact]
    public void NotFound_Error_Maps_To_404()
    {
        var result = ProblemResults.FromError(Error.NotFound("missing"));

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Equal(("missing", "not_found"), BodyOf(result));
    }

    [Fact]
    public void Conflict_Error_Maps_To_409()
    {
        var result = ProblemResults.FromError(Error.Conflict("dupe"));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Equal(("dupe", "conflict"), BodyOf(result));
    }

    [Fact]
    public void Concurrency_Error_Maps_To_409_Like_Conflict()
    {
        var result = ProblemResults.FromError(Error.Concurrency("stale"));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Equal(("stale", "concurrency"), BodyOf(result));
    }

    [Fact]
    public void Unauthorized_Error_Maps_To_401()
    {
        var result = ProblemResults.FromError(Error.Unauthorized("nope"));

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
    }

    [Fact]
    public void Forbidden_Error_Maps_To_Forbid_Result()
        => Assert.IsType<ForbidHttpResult>(ProblemResults.FromError(Error.Forbidden("no access")));

    [Theory]
    [InlineData("validation")]
    [InlineData("unexpected")]
    [InlineData("something_unrecognised")]
    public void Other_Error_Codes_Fall_Through_To_400(string code)
    {
        var result = ProblemResults.FromError(new Error(code, "boom"));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(("boom", code), BodyOf(result));
    }

    [Fact]
    public void FromError_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => ProblemResults.FromError(null!));
}
