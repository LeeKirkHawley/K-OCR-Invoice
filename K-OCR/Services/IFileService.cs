namespace K_OCR.Services
{
    public interface IFileService
    {
        public void WriteJsonToDisk(string filePath, string json);
        public string ReadJsonFromDisk(string jsonFilePath);
    }
}
