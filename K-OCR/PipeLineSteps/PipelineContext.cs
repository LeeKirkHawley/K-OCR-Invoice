using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCR.PipeLineSteps
{
    public class PipelineContext
    {
        public string InputPath { get; set; }
        public string Text { get; set; }
        public object Layout { get; set; }
        public object Table { get; set; }
        public object LineItems { get; set; }

        // Add anything else your project needs
    }
}
