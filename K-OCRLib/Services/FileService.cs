using System.IO;
using K_OCR.PipelineService;

namespace K_OCR.Services
{
    public class FileService : IFileService
    {
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
            var jsonPath = GetJsonFilePath(imagePath);
            
            if (!File.Exists(jsonPath))
            {
                return null;
            }
            
            try
            {
                var json = await File.ReadAllTextAsync(jsonPath);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<PipelineContext>(json);
            }
            catch
            {
                return null;
            }
        }
        
        public async Task SaveContextAsync(string imagePath, PipelineContext context)
        {
            var jsonPath = GetJsonFilePath(imagePath);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(context, Newtonsoft.Json.Formatting.Indented);
            await File.WriteAllTextAsync(jsonPath, json);
            System.Console.WriteLine($"[JSON Cache] Wrote file: {Path.GetFileName(jsonPath)}");
        }
        
        public bool HasCachedJson(string imagePath)
        {
            return File.Exists(GetJsonFilePath(imagePath));
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
    }
}
