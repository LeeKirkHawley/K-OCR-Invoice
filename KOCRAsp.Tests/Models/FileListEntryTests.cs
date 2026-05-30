using KOCRAsp.Models;

namespace KOCRAsp.Tests.Models;

public class FileListEntryTests
{
    [Fact]
    public void UploadedFile_AlwaysShowsGreenUploadDot()
    {
        var entry = new FileListEntry();

        Assert.Equal("dot-validated", entry.ProcessedDotClass);
        Assert.Equal("Uploaded", entry.ProcessedDotTitle);
    }

    [Fact]
    public void ValidationDot_IsHiddenUntilOcrCompletes()
    {
        var entry = new FileListEntry
        {
            HasOcrResult = false,
            IsSavedOrAccepted = false
        };

        Assert.Equal(string.Empty, entry.ValidationDotClass);
        Assert.Equal(string.Empty, entry.ValidationDotTitle);
    }

    [Fact]
    public void ValidationDot_IsOrangeAfterOcrUntilAccepted()
    {
        var entry = new FileListEntry
        {
            HasOcrResult = true,
            IsSavedOrAccepted = false
        };

        Assert.Equal("dot-suspect", entry.ValidationDotClass);
        Assert.Equal("OCR complete (validation pending)", entry.ValidationDotTitle);
    }

    [Fact]
    public void ValidationDot_TurnsGreenAfterAcceptance()
    {
        var entry = new FileListEntry
        {
            HasOcrResult = true,
            IsSavedOrAccepted = true
        };

        Assert.Equal("dot-validated", entry.ValidationDotClass);
        Assert.Equal("Validated", entry.ValidationDotTitle);
    }
}
