using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCR.PipeLineSteps
{
    public interface IPipelineStep
    {
        Task<PipelineContext> ExecuteAsync(
            PipelineContext context,
            Dictionary<string, string> parameters
        );
    }
}
