using K_OCRLib.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace K_OCRLib.Services
{
    /// <summary>
    /// Formats OCR text for storage and display by removing bounding box and raw OCR data.
    /// This ensures that ValidatedOcrText only contains the validated/edited data.
    /// </summary>
    public static class OcrTextFormatter
    {
        /// <summary>
        /// Strips bounding boxes and raw OCR text from an invoice DTO.
        /// Returns a clean copy suitable for storage in ValidatedOcrText field.
        /// </summary>
        public static InvoiceDto? StripBoundingBoxesAndRawData(InvoiceDto? invoice)
        {
            if (invoice == null)
                return null;

            try
            {
                // Serialize to JObject to filter properties
                var json = JsonConvert.SerializeObject(invoice);
                var jObject = JsonConvert.DeserializeObject<JObject>(json);
                
                if (jObject == null)
                    return invoice;

                // Remove bounding box and raw OCR fields
                jObject.Property("FieldBoundingBoxes")?.Remove();
                jObject.Property("OriginalPageWidth")?.Remove();
                jObject.Property("OriginalPageHeight")?.Remove();
                jObject.Property("PageCount")?.Remove();
                jObject.Property("OcrText")?.Remove();

                // Remove bounding boxes from items
                var itemsArray = jObject["Items"] as JArray;
                if (itemsArray != null)
                {
                    foreach (var item in itemsArray)
                    {
                        item.Value<JObject>()?.Property("BoundingBoxes")?.Remove();
                    }
                }

                // Deserialize back to InvoiceDto
                return JsonConvert.DeserializeObject<InvoiceDto>(jObject.ToString());
            }
            catch
            {
                // If filtering fails, return original
                return invoice;
            }
        }
    }
}


