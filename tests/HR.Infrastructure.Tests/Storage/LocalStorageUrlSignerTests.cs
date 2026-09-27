using System.Buffers.Text;
using System.Web;
using HR.Infrastructure.Abstractions;

namespace HR.Infrastructure.Tests.Storage;

/// <summary>
/// [P2] Unit coverage for the dev-only local-storage URL signer: the signature must bind bucket,
/// exact storage key and expiry; expire after <see cref="LocalStorageUrlSigner.Lifetime"/>; and reject
/// tampered, foreign-key, malformed or implausibly long-lived signatures.
/// </summary>
public sealed class LocalStorageUrlSignerTests
{
    private const string BaseUrl = "https://localhost:7001";
    private const string Bucket = LocalStorageBuckets.CandidateDocuments;
    private const string Key = "11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/abc123.pdf";

    private static readonly byte[] SigningKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static (LocalStorageUrlSigner Signer, ManualTimeProvider Time) CreateSut()
    {
        var time = new ManualTimeProvider(T0);
        return (new LocalStorageUrlSigner(SigningKey, time), time);
    }

    private static (string Exp, string Sig) ReadQuery(Uri url)
    {
        var query = HttpUtility.ParseQueryString(url.Query);
        return (query["exp"]!, query["sig"]!);
    }

    [Fact]
    public void CreateSignedUrl_Builds_Dev_Route_Url_With_Escaped_Key_And_Signature()
    {
        var (sut, _) = CreateSut();

        var url = sut.CreateSignedUrl(BaseUrl + "/", Bucket, "folder with space/file.pdf");

        Assert.StartsWith($"{BaseUrl}/api/dev/local-storage/{Bucket}/folder%20with%20space/file.pdf?exp=", url.AbsoluteUri);
        var (exp, sig) = ReadQuery(url);
        Assert.Equal(T0.Add(LocalStorageUrlSigner.Lifetime).ToUnixTimeSeconds().ToString(), exp);
        Assert.Equal(32, Base64Url.DecodeFromChars(sig).Length);
    }

    [Fact]
    public void IsValid_Accepts_An_Unexpired_Signature_For_The_Same_Bucket_And_Key()
    {
        var (sut, time) = CreateSut();
        var (exp, sig) = ReadQuery(sut.CreateSignedUrl(BaseUrl, Bucket, Key));

        time.Now = T0.Add(LocalStorageUrlSigner.Lifetime).AddSeconds(-1);

        Assert.True(sut.IsValid(Bucket, Key, exp, sig));
    }

    [Fact]
    public void IsValid_Rejects_An_Expired_Signature()
    {
        var (sut, time) = CreateSut();
        var (exp, sig) = ReadQuery(sut.CreateSignedUrl(BaseUrl, Bucket, Key));

        time.Now = T0.Add(LocalStorageUrlSigner.Lifetime);

        Assert.False(sut.IsValid(Bucket, Key, exp, sig));
    }

    [Fact]
    public void IsValid_Rejects_The_Signature_For_A_Different_Key()
    {
        var (sut, _) = CreateSut();
        var (exp, sig) = ReadQuery(sut.CreateSignedUrl(BaseUrl, Bucket, Key));

        Assert.False(sut.IsValid(Bucket, Key.Replace("abc123", "abc124"), exp, sig));
    }

    [Fact]
    public void IsValid_Rejects_The_Signature_For_A_Different_Bucket()
    {
        var (sut, _) = CreateSut();
        var (exp, sig) = ReadQuery(sut.CreateSignedUrl(BaseUrl, Bucket, Key));

        Assert.False(sut.IsValid(LocalStorageBuckets.Documents, Key, exp, sig));
    }

    [Fact]
    public void IsValid_Rejects_A_Tampered_Expiry()
    {
        var (sut, _) = CreateSut();
        var (exp, sig) = ReadQuery(sut.CreateSignedUrl(BaseUrl, Bucket, Key));

        var extended = (long.Parse(exp) + 60).ToString();

        Assert.False(sut.IsValid(Bucket, Key, extended, sig));
    }

    [Fact]
    public void IsValid_Rejects_A_Tampered_Signature()
    {
        var (sut, _) = CreateSut();
        var (exp, sig) = ReadQuery(sut.CreateSignedUrl(BaseUrl, Bucket, Key));

        var bytes = Base64Url.DecodeFromChars(sig);
        bytes[0] ^= 0xFF;

        Assert.False(sut.IsValid(Bucket, Key, exp, Base64Url.EncodeToString(bytes)));
    }

    [Fact]
    public void IsValid_Rejects_A_Signature_Minted_With_Another_Key()
    {
        var (sut, _) = CreateSut();
        var other = new LocalStorageUrlSigner(Enumerable.Repeat((byte)7, 32).ToArray(), new ManualTimeProvider(T0));
        var (exp, sig) = ReadQuery(other.CreateSignedUrl(BaseUrl, Bucket, Key));

        Assert.False(sut.IsValid(Bucket, Key, exp, sig));
    }

    [Fact]
    public void Default_Constructor_Uses_A_Random_Per_Instance_Key()
    {
        var a = new LocalStorageUrlSigner(TimeProvider.System);
        var b = new LocalStorageUrlSigner(TimeProvider.System);
        var (exp, sig) = ReadQuery(a.CreateSignedUrl(BaseUrl, Bucket, Key));

        Assert.True(a.IsValid(Bucket, Key, exp, sig));
        Assert.False(b.IsValid(Bucket, Key, exp, sig));
    }

    [Fact]
    public void IsValid_Rejects_An_Expiry_Beyond_The_Maximum_Lifetime()
    {
        // Even a correctly-signed URL whose expiry lies far beyond now + Lifetime cannot have been
        // minted by this signer; it is rejected rather than trusted for its full claimed duration.
        var (sut, time) = CreateSut();
        time.Now = T0.AddDays(1);
        var (exp, sig) = ReadQuery(sut.CreateSignedUrl(BaseUrl, Bucket, Key));

        time.Now = T0;

        Assert.False(sut.IsValid(Bucket, Key, exp, sig));
    }

    [Theory]
    [InlineData(null, "sig")]
    [InlineData("", "sig")]
    [InlineData("123", null)]
    [InlineData("123", "")]
    [InlineData("-5", "AAAA")]
    [InlineData("+1790000000", "AAAA")]
    [InlineData("1790000000.5", "AAAA")]
    [InlineData("99999999999999999999", "AAAA")]
    [InlineData("1790000000", "not base64url!!")]
    [InlineData("1790000000", "AAAA")]
    public void IsValid_Rejects_Malformed_Expiry_Or_Signature(string? exp, string? sig)
    {
        var (sut, _) = CreateSut();

        Assert.False(sut.IsValid(Bucket, Key, exp, sig));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-bucket")]
    [InlineData("Candidate-Documents")]
    public void CreateSignedUrl_Rejects_Unknown_Bucket(string bucket)
    {
        var (sut, _) = CreateSut();

        Assert.ThrowsAny<ArgumentException>(() => sut.CreateSignedUrl(BaseUrl, bucket, Key));
    }

    [Fact]
    public void Constructor_Rejects_A_Short_Key()
    {
        Assert.Throws<ArgumentException>(() => new LocalStorageUrlSigner(new byte[16], TimeProvider.System));
    }
}
