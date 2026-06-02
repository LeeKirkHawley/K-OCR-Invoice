using System.Text.Json;
using K_OCRLib.Configuration;
using K_OCRLib.Services.Interfaces;

namespace K_OCRLib.Services;

public class OrgConfigService : IOrgConfigService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly IPathService _pathService;

    public OrgConfigService(IPathService pathService)
    {
        _pathService = pathService;
    }

    public async Task<OrgConfig> LoadAsync(string orgName)
    {
        var path = _pathService.GetOrgConfigPath(orgName);
        if (!File.Exists(path))
            return new OrgConfig();

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<OrgConfig>(json, JsonOpts) ?? new OrgConfig();
        }
        catch
        {
            return new OrgConfig();
        }
    }

    public async Task SaveAsync(string orgName, OrgConfig config)
    {
        var path = _pathService.GetOrgConfigPath(orgName);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, JsonOpts);
        await File.WriteAllTextAsync(path, json);
    }
}
