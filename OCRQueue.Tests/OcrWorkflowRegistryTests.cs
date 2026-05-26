using Moq;
using OCRQueue.Abstractions;
using OCRQueue.Models;
using OCRQueue.Services;

namespace OCRQueue.Tests;

public class OcrWorkflowRegistryTests
{
    [Fact]
    public void Resolve_ReturnsWorkflowByKey()
    {
        var mockWorkflow = new Mock<IQueuedOcrWorkflow>();
        mockWorkflow.Setup(w => w.WorkflowKey).Returns("CustomWorkflow");

        var registry = new OcrWorkflowRegistry(new[] { mockWorkflow.Object });

        var resolved = registry.Resolve("CustomWorkflow");

        Assert.NotNull(resolved);
        Assert.Equal("CustomWorkflow", resolved.WorkflowKey);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var mockWorkflow = new Mock<IQueuedOcrWorkflow>();
        mockWorkflow.Setup(w => w.WorkflowKey).Returns("CustomWorkflow");

        var registry = new OcrWorkflowRegistry(new[] { mockWorkflow.Object });

        var resolved = registry.Resolve("customworkflow");

        Assert.NotNull(resolved);
        Assert.Equal("CustomWorkflow", resolved.WorkflowKey);
    }

    [Fact]
    public void Resolve_FallsBackToDefault()
    {
        var mockDefault = new Mock<IQueuedOcrWorkflow>();
        mockDefault.Setup(w => w.WorkflowKey).Returns("Default");

        var mockCustom = new Mock<IQueuedOcrWorkflow>();
        mockCustom.Setup(w => w.WorkflowKey).Returns("Custom");

        var registry = new OcrWorkflowRegistry(new[] { mockDefault.Object, mockCustom.Object });

        var resolved = registry.Resolve("UnknownKey");

        Assert.NotNull(resolved);
        Assert.Equal("Default", resolved.WorkflowKey);
    }

    [Fact]
    public void Resolve_ThrowsWhenNoWorkflowAndNoDefault()
    {
        var mockWorkflow = new Mock<IQueuedOcrWorkflow>();
        mockWorkflow.Setup(w => w.WorkflowKey).Returns("Custom");

        var registry = new OcrWorkflowRegistry(new[] { mockWorkflow.Object });

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Resolve("UnknownKey"));
        Assert.Contains("No OCR workflow registered", ex.Message);
    }

    [Fact]
    public void RegisteredKeys_ReturnsAllWorkflowKeys()
    {
        var mockWorkflow1 = new Mock<IQueuedOcrWorkflow>();
        mockWorkflow1.Setup(w => w.WorkflowKey).Returns("Workflow1");

        var mockWorkflow2 = new Mock<IQueuedOcrWorkflow>();
        mockWorkflow2.Setup(w => w.WorkflowKey).Returns("Workflow2");

        var registry = new OcrWorkflowRegistry(new[] { mockWorkflow1.Object, mockWorkflow2.Object });

        var keys = registry.RegisteredKeys;

        Assert.Equal(2, keys.Count);
        Assert.Contains("Workflow1", keys);
        Assert.Contains("Workflow2", keys);
    }
}
