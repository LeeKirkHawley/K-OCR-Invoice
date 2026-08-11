using K_OCRLib.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class FileServiceTests
{
    [Fact]
    public void LoadFiles_ReturnsAllFilesWhenNoExtensionFilter()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "file1.pdf"), "");
            File.WriteAllText(Path.Combine(tempDir, "file2.txt"), "");
            File.WriteAllText(Path.Combine(tempDir, "file3.doc"), "");
            
            var service = new FileService(Mock.Of<ILogger<FileService>>());
            
            var result = service.LoadFiles(tempDir).ToList();
            
            Assert.Equal(3, result.Count);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadFiles_FiltersMultipleExtensions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "file1.pdf"), "");
            File.WriteAllText(Path.Combine(tempDir, "file2.txt"), "");
            File.WriteAllText(Path.Combine(tempDir, "file3.doc"), "");
            
            var service = new FileService(Mock.Of<ILogger<FileService>>());
            
            var result = service.LoadFiles(tempDir, [".pdf", ".txt"]).ToList();
            
            Assert.Equal(2, result.Count);
            Assert.True(result.All(f => f.EndsWith(".pdf") || f.EndsWith(".txt")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadFiles_ReturnsOrderedResults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "zzz.txt"), "");
            File.WriteAllText(Path.Combine(tempDir, "aaa.txt"), "");
            File.WriteAllText(Path.Combine(tempDir, "mmm.txt"), "");
            
            var service = new FileService(Mock.Of<ILogger<FileService>>());
            
            var result = service.LoadFiles(tempDir).ToList();
            
            Assert.Equal(3, result.Count);
            Assert.True(result[0].EndsWith("aaa.txt"));
            Assert.True(result[1].EndsWith("mmm.txt"));
            Assert.True(result[2].EndsWith("zzz.txt"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
