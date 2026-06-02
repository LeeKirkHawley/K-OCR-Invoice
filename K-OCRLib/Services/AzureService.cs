using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace K_OCRLib.Services
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

        public async Task RunAzureOcrAsync(IEnumerable<string> filePaths, string? artifactsDirectory = null)
        {
            foreach (var filePath in filePaths)
            {
                string endpoint = _config["AzureCognitiveServicesEndpoint"];
                string apiKey = _config["AzureCognitiveServicesKey"];

                var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

                var url = $"{endpoint}vision/v3.2/read/analyze";

                byte[] fileBytes = File.ReadAllBytes(filePath);
                using var content = new ByteArrayContent(fileBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

                var response = await client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                string operationUrl = response.Headers.GetValues("Operation-Location").First();

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

                Console.WriteLine(resultJson);
            }
        }
    }
}
