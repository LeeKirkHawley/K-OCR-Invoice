using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OCRQueue.Abstractions;
using OCRQueue.Services;
using System.Threading.RateLimiting;

namespace OCRQueue.Tests;

/// <summary>
/// Tests for OcrQueueProcessor background service.
/// Note: Full integration tests of ExecuteAsync/ProcessLoopAsync require async test harness
/// and would test the dequeueing loop and job dispatch. These unit tests focus on
/// pause/drain logic, rateLimiter initialization, and public surface area.
/// </summary>
public class OcrQueueProcessorTests
{
    private readonly Mock<IOcrJobQueue> _mockQueue;
    private readonly Mock<IOcrQueueRepository> _mockRepository;
    private readonly OcrWorkflowRegistry _workflowRegistry;
    private readonly Mock<IOcrJobEventPublisher> _mockEventPublisher;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IPathService> _mockPathService;
    private readonly Mock<ILogger<OcrQueueProcessor>> _mockLogger;
    private readonly OcrQueueSettings _settings;

    public OcrQueueProcessorTests()
    {
        _mockQueue = new Mock<IOcrJobQueue>();
        _mockRepository = new Mock<IOcrQueueRepository>();
        // OcrWorkflowRegistry requires IEnumerable<IQueuedOcrWorkflow>
        _workflowRegistry = new OcrWorkflowRegistry(Enumerable.Empty<IQueuedOcrWorkflow>());
        _mockEventPublisher = new Mock<IOcrJobEventPublisher>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockPathService = new Mock<IPathService>();
        _mockLogger = new Mock<ILogger<OcrQueueProcessor>>();

        _settings = new OcrQueueSettings
        {
            MaxConcurrentJobs = 2,
            MaxJobsPerWindow = 5,
            WindowSeconds = 1
        };
    }

    private OcrQueueProcessor CreateProcessor()
    {
        var optionsMonitor = Options.Create(_settings);
        return new OcrQueueProcessor(
            _mockQueue.Object,
            _mockRepository.Object,
            _workflowRegistry,
            _mockEventPublisher.Object,
            _mockScopeFactory.Object,
            _mockPathService.Object,
            optionsMonitor,
            _mockLogger.Object
        );
    }

    #region Pause/Drain Tests

    [Fact]
    public async Task PauseAndDrainAsync_SetsIsPausedToTrue()
    {
        var processor = CreateProcessor();

        // IsPaused should be false initially
        Assert.False(processor.IsPaused);

        // Pause with no semaphore (not started yet)
        await processor.PauseAndDrainAsync();

        Assert.True(processor.IsPaused);
    }

    [Fact]
    public async Task PauseAndDrainAsync_WithSemaphoreAcquiresAndReleasesSlots()
    {
        var processor = CreateProcessor();
        var sem = new SemaphoreSlim(0, _settings.MaxConcurrentJobs);

        // Simulate the processor's semaphore initialization by reflection
        var semField = typeof(OcrQueueProcessor).GetField(
            "_concurrencySemaphore",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        semField?.SetValue(processor, sem);

        // Simulate two in-flight jobs completing after pause is requested.
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(50);
            sem.Release(2);
        });

        // Now pause and drain — this should wait for both releases, then return the semaphore to full capacity.
        await processor.PauseAndDrainAsync();
        await releaseTask;

        // Semaphore should be back to full capacity
        Assert.Equal(_settings.MaxConcurrentJobs, sem.CurrentCount);
    }

    [Fact]
    public async Task PauseAndDrainAsync_WithActiveCancellationTokenRespectsIt()
    {
        var processor = CreateProcessor();
        var cts = new CancellationTokenSource();
        
        // Create a semaphore that will hang waiting
        var sem = new SemaphoreSlim(0, _settings.MaxConcurrentJobs);
        var semField = typeof(OcrQueueProcessor).GetField(
            "_concurrencySemaphore",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        semField?.SetValue(processor, sem);

        // Cancel the token in 50ms
        cts.CancelAfter(50);

        // Drain should be cancelled
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(
            () => processor.PauseAndDrainAsync(cts.Token));
    }

    [Fact]
    public void Resume_SetsPausedToFalse()
    {
        var processor = CreateProcessor();
        
        // Set paused to true via reflection
        var pausedField = typeof(OcrQueueProcessor).GetField(
            "_paused",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        pausedField?.SetValue(processor, true);

        Assert.True(processor.IsPaused);

        processor.Resume();

        Assert.False(processor.IsPaused);
    }

    #endregion

    #region RateLimiter Tests

    [Fact]
    public void Constructor_InitializesRateLimiterWithSettings()
    {
        var processor = CreateProcessor();

        // Get the rate limiter via reflection
        var rateLimiterField = typeof(OcrQueueProcessor).GetField(
            "_rateLimiter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rateLimiter = rateLimiterField?.GetValue(processor) as RateLimiter;

        Assert.NotNull(rateLimiter);
        // FixedWindowRateLimiter is created with settings
        Assert.IsType<FixedWindowRateLimiter>(rateLimiter);
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_DisposesRateLimiter()
    {
        var processor = CreateProcessor();
        
        var rateLimiterField = typeof(OcrQueueProcessor).GetField(
            "_rateLimiter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rateLimiter = rateLimiterField?.GetValue(processor) as RateLimiter;

        // Dispose should not throw
        processor.Dispose();

        // Rate limiter should be disposed (acquiring should throw)
        Assert.Throws<ObjectDisposedException>(() =>
        {
            rateLimiter?.AttemptAcquire();
        });
    }

    #endregion
}
