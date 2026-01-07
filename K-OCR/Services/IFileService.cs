using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCR.Services
{
    public interface IFileService
    {
        public void WriteJsonToDisk(string filePath, string json);
        public string ReadJsonFromDisk(string jsonFilePath);
    }
}
