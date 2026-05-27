namespace KOCRAsp.Infrastructure;

public static class UrlCanonicalizer
{
    public static string GetCanonicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        var trimmed = normalized.TrimEnd('/');
        if (trimmed.Length == 0)
            return "/";

        if (trimmed.Equals("/Home/Index", StringComparison.OrdinalIgnoreCase))
            return "/";

        return trimmed.ToLowerInvariant();
    }

    public static bool NeedsRedirect(string path, out string canonicalPath)
    {
        canonicalPath = GetCanonicalPath(path);
        if (string.IsNullOrWhiteSpace(path))
            return canonicalPath != "/";

        var normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        return !string.Equals(normalized, canonicalPath, StringComparison.Ordinal);
    }
}
