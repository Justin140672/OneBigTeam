using HR.Infrastructure.Abstractions;

using Xunit;

namespace HR.Infrastructure.Tests;

public class SupabaseStorageBucketCheckTests
{
    [Fact]
    public void Bucket_Present_Matched_By_Id_Returns_True()
    {
        var body = """[{"id":"documents","name":"Documents Bucket"}]""";

        Assert.True(SupabaseStorageBucketCheck.ContainsBucket(body, "documents"));
    }

    [Fact]
    public void Bucket_Present_Matched_By_Name_Only_Returns_True()
    {
        var body = """[{"id":"internal-id-123","name":"documents"}]""";

        Assert.True(SupabaseStorageBucketCheck.ContainsBucket(body, "documents"));
    }

    [Fact]
    public void Bucket_Not_Present_In_Valid_List_Returns_False()
    {
        var body = """[{"id":"other-bucket","name":"Other Bucket"}]""";

        Assert.False(SupabaseStorageBucketCheck.ContainsBucket(body, "documents"));
    }

    [Fact]
    public void Empty_Array_Response_Returns_False()
    {
        Assert.False(SupabaseStorageBucketCheck.ContainsBucket("[]", "documents"));
    }

    [Fact]
    public void Malformed_Json_Returns_False_And_Does_Not_Throw()
    {
        Assert.False(SupabaseStorageBucketCheck.ContainsBucket("{not valid json", "documents"));
    }

    [Fact]
    public void Non_Array_Json_Object_Returns_False()
    {
        var body = """{"id":"documents","name":"documents"}""";

        Assert.False(SupabaseStorageBucketCheck.ContainsBucket(body, "documents"));
    }

    [Fact]
    public void Non_Array_Json_Scalar_Returns_False()
    {
        Assert.False(SupabaseStorageBucketCheck.ContainsBucket("\"documents\"", "documents"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_Or_Empty_Or_Whitespace_BucketName_Returns_False(string? bucketName)
    {
        var body = """[{"id":"documents","name":"documents"}]""";

        Assert.False(SupabaseStorageBucketCheck.ContainsBucket(body, bucketName!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_Or_Empty_Or_Whitespace_ResponseBody_Returns_False(string? responseBody)
    {
        Assert.False(SupabaseStorageBucketCheck.ContainsBucket(responseBody!, "documents"));
    }

    [Fact]
    public void Elements_Missing_Both_Id_And_Name_Are_Skipped_Without_Throwing()
    {
        var body = """[{"unrelated":"field"},{"id":"documents"}]""";

        Assert.True(SupabaseStorageBucketCheck.ContainsBucket(body, "documents"));
    }

    [Fact]
    public void Match_Is_Case_Sensitive_Ordinal()
    {
        var body = """[{"id":"Documents"}]""";

        Assert.False(SupabaseStorageBucketCheck.ContainsBucket(body, "documents"));
    }
}
