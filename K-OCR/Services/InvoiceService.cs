using Azure;
using Azure.AI.DocumentIntelligence;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using K_OCR.Models;
using K_OCR.Services;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Tesseract;
using System;
using System.IO;
using System.Threading.Tasks;

namespace K_OCR.Services
{
    public class InvoiceService : IInvoiceService
    {
        public InvoiceService()
        {
        }

        public async Task<string> RunAzureInvoiceParse(string imagePath)
        {

            string endpoint = "https://parsedocimage.cognitiveservices.azure.com/";
            string key = "8DfAO78fFo48z5mMerbuJ6dLGvUFLS7CcF9qUvsrCVfWPGGno5O6JQQJ99CAACrJL3JXJ3w3AAALACOGJQx4";
            string? analysisResult = "";

            var client = new DocumentIntelligenceClient(
                new Uri(endpoint),
                new AzureKeyCredential(key));

            using var stream = File.OpenRead(imagePath);
            // Construct options with model id and bytes source for the current SDK
            var options = new AnalyzeDocumentOptions("prebuilt-invoice", BinaryData.FromStream(stream))
            {
                //Features = { DocumentAnalysisFeature.OcrHighResolution }
            };

            Azure.Operation<AnalyzeResult>? operation = null;
            try
            {
                operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, options);
            }
            catch (Exception ex)
            {

            }

            AnalyzeResult result = operation!.Value;
            if (result != null)
            {
                analysisResult = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }

            Console.WriteLine("=== Invoice Fields ===");

            foreach (var doc in result.Documents)
            {
                foreach (var field in doc.Fields)
                {
                    Console.WriteLine($"{field.Key}: {field.Value?.Content}");
                }
            }

            Console.WriteLine("\n=== Line Items ===");

            foreach (var doc in result.Documents)
            {
                if (doc.Fields.TryGetValue("Items", out var itemsField) &&
                    itemsField.FieldType == DocumentFieldType.List)
                {
                    foreach (var item in itemsField.ValueList)
                    {
                        var itemFields = item.ValueDictionary;
                        foreach (var kvp in itemFields)
                        {
                            var fieldName = kvp.Key;
                            var fieldValue = kvp.Value;
                            var content = fieldValue?.Content ?? string.Empty;
                            Console.WriteLine($"{fieldName}: {content}");
                        }
                    }
                }
            }

            return analysisResult;
        }
        
    }
}
