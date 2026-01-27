using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static K_OCR.PipeLineSteps.PipeLineTextConfig;

namespace K_OCR.PipeLineSteps
{
    public class UserPipelineConfig
    {
        public string UserId { get; set; }
        public JArray Pipeline { get; set; }
    }


    public class PipelineConfigLoader
    {
        public static PipelineTextConfig Load(string path)
        {
            var json = File.ReadAllText(path);
            var userConfig = JsonConvert.DeserializeObject<UserPipelineConfig>(json);
            
            var config = new PipelineTextConfig();

            if (userConfig?.Pipeline == null)
                return config;

            foreach (var item in userConfig.Pipeline)
            {
                if (item is JObject stepObj)
                {
                    var stepName = stepObj["step"]?.ToString();
                    if (string.IsNullOrEmpty(stepName))
                        continue;

                    var parameters = new Dictionary<string, string>();
                    
                    // Extract all properties except "step" as parameters
                    foreach (var prop in stepObj.Properties())
                    {
                        if (prop.Name != "step")
                        {
                            parameters[prop.Name] = prop.Value?.ToString() ?? string.Empty;
                        }
                    }

                    config.Steps.Add(new PipelineTextConfig.StepConfig
                    {
                        Step = stepName,
                        Parameters = parameters
                    });
                }
            }

            return config;
        }
    }
}
