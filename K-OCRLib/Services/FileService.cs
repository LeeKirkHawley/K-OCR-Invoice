using System.IO;
using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Logging;


namespace K_OCRLib.Services
{
    public class FileService : IFileService
    {
        private readonly ILogger<FileService> _logger;

        public FileService(ILogger<FileService> logger)
        {
            _logger = logger;
        }
        
        public IEnumerable<string> LoadFiles(string directory, string[]? extensions = null)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return Enumerable.Empty<string>();
            } 

            if (extensions == null)
            {
                // Return all files when no extensions specified
                return Directory.EnumerateFiles(directory).OrderBy(f => f);
            }

            // HashSet for O(1) lookup instead of array O(n) - important for large directories
            var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            
            // Single directory traversal, lazy streaming, efficient filtering
            return Directory.EnumerateFiles(directory)
                .Where(f => extensionSet.Contains(Path.GetExtension(f)))
                .OrderBy(f => f);
        }
       

        public IEnumerable<DirectoryEntry> ListDirectory(string? path = null)
        {
            var entries = new List<DirectoryEntry>();
            var dir = string.IsNullOrEmpty(path) ? Directory.GetDirectoryRoot(Directory.GetCurrentDirectory()) : path;
            if (!Directory.Exists(dir))
                return entries;

            // Add directories
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                entries.Add(new DirectoryEntry
                {
                    Name = Path.GetFileName(d),
                    Path = d,
                    IsDirectory = true
                });
            }
            // Add files
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                entries.Add(new DirectoryEntry
                {
                    Name = Path.GetFileName(f),
                    Path = f,
                    IsDirectory = false
                });
            }
            return entries;
        }
    }
}
