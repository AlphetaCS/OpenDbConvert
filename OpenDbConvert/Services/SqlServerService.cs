using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using OpenDbConvert.Abstractions;
using OpenDbConvert.Models;

namespace OpenDbConvert.Services;

public class SqlServerService(ConfigurationService configurationService, LoggingService loggingService)
{
    public void ExecutePreProcessScripts()
    {
        var directory = configurationService.PreProcessSqlDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            loggingService.Log("PreProcessSqlDirectory is not configured, skipping pre-process scripts");
            return;
        }

        var sqlFiles = Directory.GetFiles(directory, "*.sql").OrderBy(Path.GetFileName).ToList();

        if (sqlFiles.Count == 0)
        {
            loggingService.Log("PreProcessSqlDirectory does not contain .sql files, skipping pre-process scripts");
            return;
        }

        using var connection = new SqlConnection(configurationService.SqlServerConnectionString);
        connection.Open();

        foreach (var file in sqlFiles)
        {
            loggingService.Log($"Executing pre-process script: {Path.GetFileName(file)}");
            try
            {
                var sql = File.ReadAllText(file);
                var serverConnection = new ServerConnection(connection);
                var server = new Server(serverConnection);
                server.ConnectionContext.ExecuteNonQuery(sql);
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Failed executing {Path.GetFileName(file)}: {ex.Message}");
                throw;
            }
        }
    }

    public SourceStructureModel PrepareSourceStructureModel()
    {
        var model = new SourceStructureModel();

        using var connection = new SqlConnection(configurationService.SqlServerConnectionString);
        connection.Open();

        // Get table names
        model.TableNames.AddRange(connection.GetSchema("Tables").Select("table_type = 'BASE TABLE'").Select(row => row["table_name"].ToString()?.ToLower()));
        model.TableNames.Sort();

        // Get foreign key structure
        model.ForeignKeyColumnInfo = new DataTable();
        var foreignKeyQuery = "SELECT foreign_keys.Name AS ConstraintName, OBJECT_NAME(foreign_key_columns.parent_object_id) AS ForeignTableName, parent_cols.Name AS ForeignColumnName, OBJECT_NAME(foreign_key_columns.referenced_object_id) AS PrimaryTableName, foreign_cols.Name AS PrimaryColumnName, foreign_keys.update_referential_action_desc AS UpdateAction, foreign_keys.delete_referential_action_desc AS DeleteAction FROM sys.foreign_keys JOIN sys.foreign_key_columns ON foreign_keys.object_id = foreign_key_columns.constraint_object_id JOIN sys.columns foreign_cols ON foreign_key_columns.referenced_object_id = foreign_cols.object_id AND foreign_key_columns.referenced_column_id = foreign_cols.column_id JOIN sys.columns parent_cols ON foreign_key_columns.parent_object_id = parent_cols.object_id AND foreign_key_columns.parent_column_id = parent_cols.column_id ORDER BY foreign_keys.name, foreign_key_columns.constraint_column_id";
        using var foreignKeyCommand = new SqlCommand(foreignKeyQuery, connection);
        var fkReader = foreignKeyCommand.ExecuteReader();
        model.ForeignKeyColumnInfo.Load(fkReader);
        model.ForeignKeyNames = model.ForeignKeyColumnInfo.AsEnumerable().Select(r => r.Field<string>("ConstraintName").ToLower()).Distinct().ToList();
        model.ForeignKeyNames.Sort();

        // Get index structure
        model.IndexColumnInfo = new DataTable();
        var indexQuery = "SELECT OBJECT_NAME(indexes.object_id) AS tableName, indexes.Name AS indexName, columns.Name AS columnName, indexes.is_unique AS isUnique FROM sys.indexes JOIN sys.index_columns ON indexes.object_id = index_columns.object_id AND indexes.index_id = index_columns.index_id JOIN sys.columns ON index_columns.object_id = columns.object_id AND index_columns.column_id = columns.column_id JOIN sys.objects ON indexes.object_id = sys.objects.object_id WHERE is_primary_key = 0 AND sys.objects.type = 'U' ORDER BY 1, index_columns.key_ordinal, index_columns.index_column_id";
        using var indexCommand = new SqlCommand(indexQuery, connection);
        var ixReader = indexCommand.ExecuteReader();
        model.IndexColumnInfo.Load(ixReader);
        model.IndexNames = model.IndexColumnInfo.AsEnumerable().Select(r => r.Field<string>("indexName").ToLower()).Distinct().ToList();
        model.IndexNames.Sort();

        return model;
    }

    public string GetMySqlCreateStatement(string tableName)
    {
        using var connection = new SqlConnection(configurationService.SqlServerConnectionString);
        connection.Open();

        // Retrieve column metadata
        var columnQuery = "SELECT c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.IS_NULLABLE, c.COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = @tableName ORDER BY c.ORDINAL_POSITION";
        var columns = new List<(string name, string dataType, int? charLength, int? precision, int? scale, bool isNullable, string columnDefault)>();

        using (var command = new SqlCommand(columnQuery, connection))
        {
            command.Parameters.AddWithValue("@tableName", tableName);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var dataType = reader.GetString(1).ToLower();
                var charLength = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                var precision = reader.IsDBNull(3) ? (int?)null : reader.GetByte(3);
                var scale = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var isNullable = reader.GetString(5).Equals("YES", StringComparison.OrdinalIgnoreCase);
                var columnDefault = reader.IsDBNull(6) ? null : reader.GetString(6);
                columns.Add((name, dataType, charLength, precision, scale, isNullable, columnDefault));
            }
        }

        // Retrieve primary key columns
        var primaryKeyQuery = "SELECT kcu.COLUMN_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME AND tc.TABLE_NAME = kcu.TABLE_NAME WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_NAME = @tableName ORDER BY kcu.ORDINAL_POSITION";
        var primaryKeyColumns = new List<string>();

        using (var command = new SqlCommand(primaryKeyQuery, connection))
        {
            command.Parameters.AddWithValue("@tableName", tableName);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                primaryKeyColumns.Add(reader.GetString(0));
        }

        // Build CREATE TABLE statement
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE `{tableName}` (");

        var columnDefinitions = new List<string>();
        foreach (var col in columns)
        {
            var mysqlType = ResolveMySqlType(col.dataType, col.charLength, col.precision, col.scale);
            var nullability = col.isNullable ? "NULL" : "NOT NULL";
            var defaultClause = string.Empty;

            if (col.columnDefault != null)
            {
                var cleanDefault = col.columnDefault.Trim().Trim('(', ')').Trim('\'');

                if (col.columnDefault.Contains("getdate", StringComparison.OrdinalIgnoreCase) ||
                    col.columnDefault.Contains("getutcdate", StringComparison.OrdinalIgnoreCase))
                    defaultClause = " DEFAULT CURRENT_TIMESTAMP";
                else if (col.dataType == "bit")
                    defaultClause = $" DEFAULT {(cleanDefault.EndsWith("0") ? "0" : "1")}";
                else if (col.dataType is "int" or "bigint" or "smallint" or "tinyint" or "float" or "real" or "decimal" or "numeric" or "money" or "smallmoney")
                    defaultClause = $" DEFAULT {cleanDefault}";
                else
                    defaultClause = $" DEFAULT '{cleanDefault}'";
            }

            columnDefinitions.Add($"    `{col.name}` {mysqlType} {nullability}{defaultClause}");
        }

        if (primaryKeyColumns.Count > 0)
        {
            var pkCols = string.Join(", ", primaryKeyColumns.Select(c => $"`{c}`"));
            columnDefinitions.Add($"    PRIMARY KEY ({pkCols})");
        }

        sb.Append(string.Join($",{Environment.NewLine}", columnDefinitions));
        sb.AppendLine();
        sb.Append(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        return sb.ToString();
    }

    private static string ResolveMySqlType(string dataType, int? charLength, int? precision, int? scale)
    {
        switch (dataType)
        {
            case "varchar":
            case "nvarchar":
                return charLength is null or -1 ? "longtext" : $"varchar({charLength})";
            case "char":
            case "nchar":
                return charLength.HasValue ? $"char({charLength})" : "char(1)";
            case "varbinary":
                return charLength is null or -1 ? "longblob" : $"varbinary({charLength})";
            case "binary":
                return charLength.HasValue ? $"binary({charLength})" : "binary(1)";
            case "decimal":
            case "numeric":
                return $"decimal({precision ?? 18},{scale ?? 0})";
        }

        return ConversionMap.Map.GetValueOrDefault(dataType, "longtext");
    }

    public DataTable GetTableData(string tableName)
    {
        using var connection = new SqlConnection(configurationService.SqlServerConnectionString);
        connection.Open();

        var table = new DataTable();
        using var command = new SqlCommand($"SELECT * FROM [{tableName}]", connection);
        using var reader = command.ExecuteReader();
        table.Load(reader);
        return table;
    }

    public bool CanConnect()
    {
        try
        {
            using var connection = new SqlConnection(configurationService.SqlServerConnectionString);
            connection.Open();
            return true;
        }
        catch(Exception ex)
        {
            loggingService.LogError(ex.Message);
            return false;
        }
    }
}
