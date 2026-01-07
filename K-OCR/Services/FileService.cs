using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        }

        public string ReadJsonFromDisk(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException($"JSON file not found at path: {jsonFilePath}");
            }
            return File.ReadAllText(jsonFilePath);
        }

    }
}
