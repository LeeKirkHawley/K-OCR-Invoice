using KOCRAsp.Infrastructure;

namespace KOCRAsp.Tests.Infrastructure;

public class UrlCanonicalizerTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/Home/Index", "/")]
    [InlineData("/Home/Index/", "/")]
    [InlineData("/AUTH/Login/", "/auth/login")]
    [InlineData("/Settings/Index", "/settings/index")]
    public void GetCanonicalPath_NormalizesExpectedPaths(string input, string expected)
    {
        Assert.Equal(expected, UrlCanonicalizer.GetCanonicalPath(input));
    }

    [Theory]
    [InlineData("/", false)]
    [InlineData("/home/index", true)]
    [InlineData("/Home/Index", true)]
    [InlineData("/AUTH/Login/", true)]
    [InlineData("/auth/login", false)]
    public void NeedsRedirect_DetectsNonCanonicalPaths(string input, bool expected)
    {
        Assert.Equal(expected, UrlCanonicalizer.NeedsRedirect(input, out _));
    }
}
