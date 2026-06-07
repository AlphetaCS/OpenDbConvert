using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using OpenDbConvert.Infrastructure;
using OpenDbConvert.Services;

#region Service Registration
var services = new ServiceCollection();
ServiceRegistrar.RegisterServices(services);
var provider = services.BuildServiceProvider();
var configurationService = provider.GetRequiredService<ConfigurationService>();
var loggingService = provider.GetRequiredService<LoggingService>();
var sqlServerService = provider.GetRequiredService<SqlServerService>();
var mysqlService = provider.GetRequiredService<MySqlService>();
#endregion

#region Configuration
loggingService.Log("=== Loading configuration ===");
if (string.IsNullOrWhiteSpace(configurationService.SqlServerConnectionString))
{
    loggingService.LogError("Configuration error: SourceConnectionString is required");
    Environment.Exit(1);
}

if (string.IsNullOrWhiteSpace(configurationService.MySqlConnectionString))
{
    loggingService.LogError("Configuration error: DestinationConnectionString is required");
    Environment.Exit(1);
}
#endregion

#region Testing Connectivity
loggingService.Log("=== Verifying SQL Server connectivity ===");
if (!sqlServerService.CanConnect()) Environment.Exit(1);

loggingService.Log("=== Verifying MySQL Server connectivity ===");
if (!mysqlService.CanConnect()) Environment.Exit(1);
#endregion

#region Pre SQL Processing
loggingService.Log("=== Checking PreProcess Scripts ===");
sqlServerService.ExecutePreProcessScripts();
#endregion

#region SQL Server Structure Retrieval
loggingService.Log("=== Reading source structures ===");
var sourceStructure = sqlServerService.PrepareSourceStructureModel();
loggingService.Log($"Found {sourceStructure.TableNames.Count} tables, {sourceStructure.ForeignKeyNames.Count} foreign keys, {sourceStructure.IndexNames.Count} indexes");
#endregion

#region Clear MySQL Structures
loggingService.Log("=== Clearing destination structures ===");
mysqlService.ClearStructures();
#endregion

#region Create and Import Table Data
loggingService.Log("=== Creating tables ===");
var tableCreationSyntax = sourceStructure.TableNames.Select(sqlServerService.GetMySqlCreateStatement).ToList();
try
{
    mysqlService.CreateTables(tableCreationSyntax);
}
catch (Exception ex)
{
    loggingService.LogError(ex.Message);
    Environment.Exit(1);
}
loggingService.Log($"Created {tableCreationSyntax.Count} table{(tableCreationSyntax.Count == 1 ? string.Empty : "s")}");

loggingService.Log("=== Importing table data ===");
foreach (var tableName in sourceStructure.TableNames)
{
    loggingService.Log($"Importing table {tableName}");
    var tableData = sqlServerService.GetTableData(tableName);
    mysqlService.ImportTableData(tableName, tableData);
}
#endregion

#region Create FKs
loggingService.Log("=== Creating foreign keys ===");
mysqlService.CreateForeignKeys(sourceStructure);
#endregion

#region Create Indexes
loggingService.Log("=== Creating indexes ===");
mysqlService.CreateIndexes(sourceStructure);
#endregion

#region Post SQL Processing
loggingService.Log("=== Checking PostProcess Scripts ===");
mysqlService.ExecutePostProcessScripts();
#endregion

loggingService.Log("=== Conversion complete ===");
