namespace OpenDbConvert.Models;

public class ConfigurationModel
{
    public string SqlServerConnectionString { get; set; } = string.Empty;
    public string MySqlConnectionString { get; set; } = string.Empty;
    public string PreProcessSqlDirectory { get; set; } = string.Empty;
    public string PostProcessSqlDirectory { get; set; } = string.Empty;
    public string LogOutputFile { get; set; } = string.Empty;
}
