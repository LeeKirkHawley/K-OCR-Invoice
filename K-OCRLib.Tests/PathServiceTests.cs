using K_OCR.Services;
using Microsoft.Extensions.Configuration;

namespace K_OCRLib.Tests;

public class PathServiceTests
{
    [Fact]
    public void SanitizeName_ReplacesUnsupportedCharacters()
    {
        var service = CreateService("D:\\Data");

        Assert.Equal("Acme_Inc___2026_", service.SanitizeName("Acme Inc. #2026!"));
    }

    [Fact]
    public void GetOrgAndBatchPaths_UsesSanitizedNames()
    {
        var service = CreateService("D:\\Data");

        Assert.Equal(@"D:\Data\Test_Org", service.GetOrgFolderPath("Test Org"));
        Assert.Equal(@"D:\Data\Test_Org\kocr.db", service.GetOrgDbPath("Test Org"));
        Assert.Equal(@"D:\Data\Test_Org\OrgConfig.json", service.GetOrgConfigPath("Test Org"));
        Assert.Equal(@"D:\Data\Test_Org\Batch_1", service.GetBatchFolderPath("Test Org", "Batch 1"));
        Assert.Equal(@"D:\Data\Test_Org\Batch_1\Invoices", service.GetInvoicesFolderPath("Test Org", "Batch 1"));
        Assert.Equal(@"D:\Data\Test_Org\Batch_1\Artifacts", service.GetArtifactsFolderPath("Test Org", "Batch 1"));
    }

    private static PathService CreateService(string baseDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kocr:BaseDirectory"] = baseDirectory
            })
            .Build();

        return new PathService(configuration);
    }
}
