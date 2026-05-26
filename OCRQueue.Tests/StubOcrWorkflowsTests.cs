using OCRQueue.Models;
using OCRQueue.Workflows;

namespace OCRQueue.Tests;

public class StubOcrWorkflowsTests
{
    private static readonly OcrJob SampleJob = new(
        JobId: 1,
        InvoiceId: 100,
        BatchId: 10,
        OrgId: "org-1",
        OrgName: "TestOrg",
        FilePath: "/path/to/file.pdf",
        WorkflowKey: "Default",
        QueuedAtUtc: DateTime.UtcNow);

    [Fact]
    public void AzureOnlyOcrWorkflow_ExposesExpectedWorkflowKey()
    {
        var workflow = new AzureOnlyOcrWorkflow();

        Assert.Equal("AzureOnly", workflow.WorkflowKey);
    }

    [Fact]
    public async Task AzureOnlyOcrWorkflow_ExecuteAsync_ThrowsNotImplemented()
    {
        var workflow = new AzureOnlyOcrWorkflow();

        var ex = await Assert.ThrowsAsync<NotImplementedException>(() =>
            workflow.ExecuteAsync(SampleJob, CancellationToken.None));

        Assert.Contains("AzureOnly workflow is not yet implemented", ex.Message);
    }

    [Fact]
    public void TesseractOnlyOcrWorkflow_ExposesExpectedWorkflowKey()
    {
        var workflow = new TesseractOnlyOcrWorkflow();

        Assert.Equal("TesseractOnly", workflow.WorkflowKey);
    }

    [Fact]
    public async Task TesseractOnlyOcrWorkflow_ExecuteAsync_ThrowsNotImplemented()
    {
        var workflow = new TesseractOnlyOcrWorkflow();

        var ex = await Assert.ThrowsAsync<NotImplementedException>(() =>
            workflow.ExecuteAsync(SampleJob, CancellationToken.None));

        Assert.Contains("TesseractOnly workflow is not yet implemented", ex.Message);
    }
}
