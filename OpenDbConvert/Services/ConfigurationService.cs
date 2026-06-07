using System.IO;
using System.Text.Json;
using OpenDbConvert.Models;

namespace OpenDbConvert.Services;

public class ConfigurationService
{
    private const string ConfigFileName = "config.json";

    private readonly ConfigurationModel _config;

    public ConfigurationService()
    {
        var configJson = File.ReadAllText(ConfigFileName);
        _config = JsonSerializer.Deserialize<ConfigurationModel>(configJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public string SqlServerConnectionString => _config.SqlServerConnectionString;
    public string MySqlConnectionString => _config.MySqlConnectionString;
    public string PreProcessSqlDirectory => _config.PreProcessSqlDirectory;
    public string PostProcessSqlDirectory => _config.PostProcessSqlDirectory;
    public string LogOutputFile => _config.LogOutputFile;
}
