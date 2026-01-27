using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCR.PipeLineSteps
{
    public class PipelineTextConfig
    {
        public class StepConfig
        {
            public string Step { get; set; }
            public Dictionary<string, string> Parameters { get; set; }
        }

        public List<StepConfig> Steps { get; set; } = new();
    }

    public static class PipelineTextConfigLoader
    {
        public static PipelineTextConfig Load(string path)
        {
            var config = new PipelineTextConfig();

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var stepName = tokens[0];

                var parameters = new Dictionary<string, string>();

                foreach (var token in tokens.Skip(1))
                {
                    var parts = token.Split('=', 2);
                    if (parts.Length == 2)
                        parameters[parts[0]] = parts[1];
                }

                config.Steps.Add(new PipelineTextConfig.StepConfig
                {
                    Step = stepName,
                    Parameters = parameters
                });
            }

            return config;
        }
    }
}
