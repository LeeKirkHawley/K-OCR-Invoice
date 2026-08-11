using K_OCRLib.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class FileServiceCamelCaseTests
{
    [Fact]
    public void LoadFiles_ReturnsEmptyWhenDirectoryDoesNotExist()
    {
        var service = new FileService(Mock.Of<ILogger<FileService>>());
        
        var result = service.LoadFiles("/nonexistent/path");
        
        Assert.Empty(result);
    }

    [Fact]
    public void LoadFiles_ReturnsFilesWithMatchingExtension()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "test1.pdf"), "");
            File.WriteAllText(Path.Combine(tempDir, "test2.pdf"), "");
            File.WriteAllText(Path.Combine(tempDir, "test3.txt"), "");
            
            var service = new FileService(Mock.Of<ILogger<FileService>>());
            
            var result = service.LoadFiles(tempDir, [".pdf"]).ToList();
            
            Assert.Equal(2, result.Count);
            Assert.True(result.All(f => f.EndsWith(".pdf")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ListDirectory_ReturnsEmptyWhenDirectoryDoesNotExist()
    {
        var service = new FileService(Mock.Of<ILogger<FileService>>());
        
        var result = service.ListDirectory("/nonexistent/path");
        
        Assert.Empty(result);
    }

    [Fact]
    public void ListDirectory_ReturnsBothFilesAndDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "subdir"));
            File.WriteAllText(Path.Combine(tempDir, "file.txt"), "");
            
            var service = new FileService(Mock.Of<ILogger<FileService>>());
            
            var result = service.ListDirectory(tempDir).ToList();
            
            Assert.Equal(2, result.Count);
            Assert.Single(result.Where(e => e.IsDirectory));
            Assert.Single(result.Where(e => !e.IsDirectory));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
