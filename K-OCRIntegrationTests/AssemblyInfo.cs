using Xunit;

// Several tests here touch the real filesystem (temp org folders, per-org SQLite files);
// keep them from stepping on each other by disabling parallelization, matching K-OCRLib.Tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
