using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace K_OCRDesktop.PipeLineSteps
{
    public class LayoutDetectionStep : IPipelineStep
    {
        public async Task<PipelineContext> ExecuteAsync(
            PipelineContext context,
            Dictionary<string, string> parameters)
        {
            var model = parameters.GetValueOrDefault("model", "doclayout_yolo");
            var conf = float.Parse(parameters.GetValueOrDefault("confidence", "0.4"));

            //context.Layout = await MyLayoutDetector.RunAsync(context.InputPath, model, conf);
            return await Task.FromResult(context);
        }
    }
}
