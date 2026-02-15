using System.IO;
using K_OCR.Models;
using K_OCR.PipelineService;
using System.IO;
using K_OCR.PipelineService;


namespace K_OCR.Services
{
    public class FileService : IFileService
    {
        private readonly DatabaseService _databaseService;

        public FileService(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }
        public void WriteJsonToDisk(string filePath, string json)
        {
            // Get directory and build new path with .json extension
            string directory = System.IO.Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(filePath);
            string jsonFilePath = System.IO.Path.Combine(directory, $"{fileNameWithoutExtension}.json");

            File.WriteAllText(jsonFilePath, json);
            System.Console.WriteLine($"[JSON Cache] Wrote file: {Path.GetFileName(jsonFilePath)}");
        }

        public string ReadJsonFromDisk(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException($"JSON file not found at path: {jsonFilePath}");
            }
            return File.ReadAllText(jsonFilePath);
        }
        
        public string GetJsonFilePath(string imagePath)
        {
            return Path.ChangeExtension(imagePath, ".json");
        }
        
        public async Task<PipelineContext?> LoadCachedContextAsync(string imagePath)
        {
            var ocrFile = await _databaseService.GetOCRFileByPathAsync(imagePath);
            if (ocrFile != null)
            {
                PipelineContext? context = null;

                // Try ValidatedOcrText first if it exists
                if (!string.IsNullOrEmpty(ocrFile.ValidatedOcrText))
                {
                    try
                    {
                        context = Newtonsoft.Json.JsonConvert.DeserializeObject<PipelineContext>(ocrFile.ValidatedOcrText);
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[Database Cache] Failed to deserialize ValidatedOcrText for {Path.GetFileName(imagePath)}: {ex.Message}");
                    }
                }

                // If ValidatedOcrText didn't work or doesn't exist, try OcrText
                if (context == null && !string.IsNullOrEmpty(ocrFile.OcrText))
                {
                    try
                    {
                        context = Newtonsoft.Json.JsonConvert.DeserializeObject<PipelineContext>(ocrFile.OcrText);
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[Database Cache] Failed to deserialize OcrText for {Path.GetFileName(imagePath)}: {ex.Message}");
                    }
                }

                return context;
            }

            return null;
        }
        
        public async Task SaveContextAsync(string imagePath, PipelineContext context)
        {
            if (_databaseService == null)
            {
                throw new InvalidOperationException("Database service is required for saving OCR context");
            }

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(context, Newtonsoft.Json.Formatting.Indented);
            
            var ocrFile = new OCRFile
            {
                FilePath = imagePath,
                OcrText = json
            };
            
            await _databaseService.SaveOCRFileAsync(ocrFile);
            System.Console.WriteLine($"[Database Cache] Saved OCR data for: {Path.GetFileName(imagePath)}");
        }
        
        public bool HasCachedJson(string imagePath)
        {
            var ocrFile = _databaseService.GetOCRFileByPathAsync(imagePath).GetAwaiter().GetResult();
            return ocrFile != null && (!string.IsNullOrEmpty(ocrFile.OcrText) || !string.IsNullOrEmpty(ocrFile.ValidatedOcrText));
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
