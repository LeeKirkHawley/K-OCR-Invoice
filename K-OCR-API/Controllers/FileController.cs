using K_OCR.Models;
using Microsoft.AspNetCore.Mvc;
using K_OCR.Services;


namespace K_OCR_API.Controllers;

[ApiController]
[Route("[controller]")]
public class FileController : ControllerBase
{
    public IFileService _fileService;

    public FileController(IFileService fileService)
    {
        _fileService = fileService;
    }

    [HttpGet(Name = "GetFiles")]
    public IEnumerable<string> Get([FromQuery] string? directory = null, [FromQuery] string[]? extensions = null)
    {
        Console.WriteLine($"[FileController] directory: '{directory ?? "NULL"}'");
        Console.WriteLine($"[FileController] extensions: {(extensions == null ? "NULL" : string.Join(", ", extensions))}");
        
        var dir = directory ?? "/home/kirk-hawley/Downloads";
        var files = _fileService.LoadFiles(dir, extensions).ToList();
        
        Console.WriteLine($"[FileController] Found {files.Count} files");
        return files;
    }

    [HttpGet("ListDirectory")]
    public IEnumerable<DirectoryEntry> ListDirectory([FromQuery] string? path = null)
    {
        Console.WriteLine($"[FileController] ListDirectory path: '{path ?? "ROOT"}'");
        return _fileService.ListDirectory(path);
    }
}
