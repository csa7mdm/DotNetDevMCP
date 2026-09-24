using DotNetDevMCP.Server;

namespace DotNetDevMCP.Integration.Tests;

/// <summary>Unit tests for the <c>--allowed-origin</c> value validator/normalizer.</summary>
public class AllowedOriginValidationTests
{
    [Fact]
    public void Null_literal_is_rejected()
    {
        var error = AllowedOriginValidation.Validate("null", out _);
        Assert.NotNull(error);
    }

    [Fact]
    public void Wildcard_is_rejected()
    {
        var error = AllowedOriginValidation.Validate("http://*.example.com", out _);
        Assert.NotNull(error);
    }

    [Fact]
    public void Trailing_slash_is_normalized_away()
    {
        var error = AllowedOriginValidation.Validate("http://localhost:5173/", out var normalized);
        Assert.Null(error);
        Assert.Equal("http://localhost:5173", normalized);
    }

    [Fact]
    public void Path_is_rejected()
    {
        var error = AllowedOriginValidation.Validate("http://localhost:5173/app", out _);
        Assert.NotNull(error);
    }

    [Fact]
    public void Https_is_accepted()
    {
        var error = AllowedOriginValidation.Validate("https://localhost:5173", out var normalized);
        Assert.Null(error);
        Assert.Equal("https://localhost:5173", normalized);
    }

    [Fact]
    public void Ftp_scheme_is_rejected()
    {
        var error = AllowedOriginValidation.Validate("ftp://localhost", out _);
        Assert.NotNull(error);
    }

    [Fact]
    public void Query_string_is_rejected()
    {
        var error = AllowedOriginValidation.Validate("http://localhost:5173?x=1", out _);
        Assert.NotNull(error);
    }

    [Fact]
    public void Userinfo_is_rejected()
    {
        var error = AllowedOriginValidation.Validate("http://user:pass@localhost:5173", out _);
        Assert.NotNull(error);
    }

    [Fact]
    public void Bare_origin_without_trailing_slash_round_trips_unchanged()
    {
        var error = AllowedOriginValidation.Validate("http://a", out var normalized);
        Assert.Null(error);
        Assert.Equal("http://a", normalized);
    }
}
