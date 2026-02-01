using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCR.PipelineService
{
    public class OcrStep : IPipelineStep
    {
        public async Task<PipelineContext> ExecuteAsync(
            PipelineContext context,
            Dictionary<string, string> parameters)
        {
            var engine = parameters.GetValueOrDefault("engine", "tesseract");
            var lang = parameters.GetValueOrDefault("language", "eng");

            //context.Text = await MyOcrEngine.RunAsync(context.InputPath, engine, lang);
            return await Task.FromResult(context);
        }
    }
}
