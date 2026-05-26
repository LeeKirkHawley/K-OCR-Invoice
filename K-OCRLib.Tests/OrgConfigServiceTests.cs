using K_OCR.Configuration;
using K_OCR.Services;
using Moq;

namespace K_OCRLib.Tests;

public class OrgConfigServiceTests
{
    [Fact]
    public async Task LoadAsync_ReturnsDefaultsWhenMissing()
    {
        var pathService = new Mock<IPathService>();
        pathService.Setup(p => p.GetOrgConfigPath("TestOrg")).Returns(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var service = new OrgConfigService(pathService.Object);

        var config = await service.LoadAsync("TestOrg");

        Assert.Equal("Default", config.OcrWorkflowKey);
        Assert.True(config.RequireBatchValidationForExport);
    }

    [Fact]
    public async Task SaveAsyncThenLoadAsync_RoundTripsConfiguration()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"orgcfg-{Guid.NewGuid()}");
        var pathService = new Mock<IPathService>();
        pathService.Setup(p => p.GetOrgConfigPath("TestOrg")).Returns(Path.Combine(baseDir, "OrgConfig.json"));
        var service = new OrgConfigService(pathService.Object);

        try
        {
            var expected = new OrgConfig
            {
                MinConfidenceThreshold = 0.9,
                RequireBatchValidationForExport = false,
                OcrWorkflowKey = "TesseractOnly"
            };

            await service.SaveAsync("TestOrg", expected);
            var loaded = await service.LoadAsync("TestOrg");

            Assert.Equal(expected.MinConfidenceThreshold, loaded.MinConfidenceThreshold);
            Assert.Equal(expected.RequireBatchValidationForExport, loaded.RequireBatchValidationForExport);
            Assert.Equal(expected.OcrWorkflowKey, loaded.OcrWorkflowKey);
        }
        finally
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, recursive: true);
        }
    }
}
