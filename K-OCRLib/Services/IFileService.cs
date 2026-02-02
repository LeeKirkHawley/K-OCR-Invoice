using K_OCR.PipelineService;

namespace K_OCR.Services
{
    public interface IFileService
    {
        public void WriteJsonToDisk(string filePath, string json);
        public string ReadJsonFromDisk(string jsonFilePath);
        
        /// <summary>
        /// Get the JSON file path for a given image file path
        /// </summary>
        string GetJsonFilePath(string imagePath);
        
        /// <summary>
        /// Load cached pipeline context from JSON file
        /// </summary>
        Task<PipelineContext?> LoadCachedContextAsync(string imagePath);
        
        /// <summary>
        /// Save pipeline context to JSON file
        /// </summary>
        Task SaveContextAsync(string imagePath, PipelineContext context);
        
        /// <summary>
        /// Check if cached JSON exists for an image
        /// </summary>
        bool HasCachedJson(string imagePath);
    }
}
