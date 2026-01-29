using K_OCR.Models;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace K_OCR.Services
{
    public class AzureService : IAzureService
    {
        private readonly IConfiguration _config;
        private readonly IFileService _fileService;

        public AzureService(IConfiguration config, IFileService fileService)
        {
            _config = config;
            _fileService = fileService;
        }

        public async Task RunAzureOcrAsync(IEnumerable<OCRFile> items)
        {
            foreach (OCRFile ocrFile in items)
            {

                string endpoint = _config["AzureCognitiveServicesEndpoint"];
                string apiKey = _config["AzureCognitiveServicesKey"];

                string filePath = ocrFile.filePath;


                var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

                var url = $"{endpoint}vision/v3.2/read/analyze";

                byte[] fileBytes = File.ReadAllBytes(filePath);
                using var content = new ByteArrayContent(fileBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");  // WILL CHANGE PER IMAGE TYPE

                // 1. Submit OCR job
                var response = await client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                // 2. Get operation URL
                string operationUrl = response.Headers.GetValues("Operation-Location").First();

                // 3. Poll until OCR completes
                string resultJson = "";
                while (true)
                {
                    await Task.Delay(1000);

                    var resultResponse = await client.GetAsync(operationUrl);
                    resultJson = await resultResponse.Content.ReadAsStringAsync();

                    using var doc = JsonDocument.Parse(resultJson);
                    string status = doc.RootElement.GetProperty("status").GetString();

                    if (status == "succeeded" || status == "failed")
                        break;
                }

                if (resultJson.Length > 0)
                {
                    _fileService.WriteJsonToDisk(filePath, resultJson);
                }

                Console.WriteLine(resultJson);
            }
        }


    }
}
