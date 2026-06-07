using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using MySql.Data.MySqlClient;
using OpenDbConvert.Models;

namespace OpenDbConvert.Services;

public class MySqlService(ConfigurationService configurationService, LoggingService loggingService)
{
    public void CreateTables(List<string> tableCreates)
    {
        using var connection = new MySqlConnection(configurationService.MySqlConnectionString);
        connection.Open();
        foreach (var tableCreateSql in tableCreates)
        {
            var tableName = tableCreateSql.Split('`').Skip(1).FirstOrDefault() ?? "unknown";
            loggingService.Log($"Creating table {tableName}");
            using var command = new MySqlCommand(tableCreateSql, connection);
            command.ExecuteNonQuery();
        }
    }

    public void ExecutePostProcessScripts()
    {
        var directory = configurationService.PostProcessSqlDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            loggingService.Log("PostProcessSqlDirectory is not configured, skipping post-process scripts");
            return;
        }

        var sqlFiles = Directory.GetFiles(directory, "*.sql").OrderBy(Path.GetFileName).ToList();

        if (sqlFiles.Count == 0)
        {
            loggingService.Log("PostProcessSqlDirectory does not contain .sql files, skipping post-process scripts");
            return;
        }

        using var connection = new MySqlConnection(configurationService.MySqlConnectionString);
        connection.Open();

        foreach (var file in sqlFiles)
        {
            loggingService.Log($"Executing post-process script: {Path.GetFileName(file)}");
            try
            {
                var sql = File.ReadAllText(file);
                using var command = new MySqlCommand(sql, connection);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Failed executing {Path.GetFileName(file)}: {ex.Message}");
                throw;
            }
        }
    }

    public void ClearStructures()
    {
        using var connection = new MySqlConnection(configurationService.MySqlConnectionString);
        connection.Open();

        #region Drop Foreign Keys
        var database = connection.Database;

        var foreignKeySql = $"SELECT TABLE_NAME, CONSTRAINT_NAME FROM information_schema.referential_constraints WHERE CONSTRAINT_SCHEMA = '{database}'";
        var foreignKeys = new List<(string table, string constraint)>();

        using (var command = new MySqlCommand(foreignKeySql, connection))
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
                foreignKeys.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var (table, constraint) in foreignKeys)
        {
            loggingService.Log($"Dropping foreign key {constraint} on table {table}");
            using var command = new MySqlCommand($"ALTER TABLE `{table}` DROP FOREIGN KEY `{constraint}`", connection);
            command.ExecuteNonQuery();
        }
        #endregion

        #region Drop Tables
        var tableSql = $"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{database}' AND TABLE_TYPE = 'BASE TABLE'";
        var tables = new List<string>();

        using (var command = new MySqlCommand(tableSql, connection))
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            loggingService.Log($"Dropping table {table}");
            using var command = new MySqlCommand($"DROP TABLE IF EXISTS `{table}`", connection);
            command.ExecuteNonQuery();
        }
        #endregion
    }

    public void ImportTableData(string tableName, DataTable data)
    {
        if (data.Rows.Count == 0) return;

        using var connection = new MySqlConnection(configurationService.MySqlConnectionString);
        connection.Open();

        var columnNames = data.Columns.Cast<DataColumn>().Select(c => $"`{c.ColumnName}`").ToList();
        var paramNames = data.Columns.Cast<DataColumn>().Select(c => $"@{c.ColumnName}").ToList();
        var insertSql = $"INSERT INTO `{tableName}` ({string.Join(", ", columnNames)}) VALUES ({string.Join(", ", paramNames)})";

        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (DataRow row in data.Rows)
            {
                using var command = new MySqlCommand(insertSql, connection, transaction);
                foreach (DataColumn column in data.Columns)
                    command.Parameters.AddWithValue($"@{column.ColumnName}", row[column] == DBNull.Value ? DBNull.Value : row[column]);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch(Exception ex)
        {
            transaction.Rollback();
            loggingService.LogError(ex.Message);
            throw;
        }
    }

    public void CreateForeignKeys(SourceStructureModel sourceStructure)
    {
        if (sourceStructure.ForeignKeyNames.Count == 0) return;

        using var connection = new MySqlConnection(configurationService.MySqlConnectionString);
        connection.Open();

        var foreignKeyGroups = sourceStructure.ForeignKeyColumnInfo.AsEnumerable()
            .GroupBy(row => row.Field<string>("ConstraintName"));

        foreach (var group in foreignKeyGroups)
        {
            var constraintName = group.Key.Length > 64 ? group.Key.Substring(0, 64) : group.Key;
            var foreignTable = group.First().Field<string>("ForeignTableName").ToLower();
            var primaryTable = group.First().Field<string>("PrimaryTableName").ToLower();
            var updateAction = group.First().Field<string>("UpdateAction").Replace("_", " ");
            var deleteAction = group.First().Field<string>("DeleteAction").Replace("_", " ");

            var foreignColumns = string.Join(", ", group.Select(r => $"`{r.Field<string>("ForeignColumnName")}`"));
            var primaryColumns = string.Join(", ", group.Select(r => $"`{r.Field<string>("PrimaryColumnName")}`"));

            var sql = $"ALTER TABLE `{foreignTable}` ADD CONSTRAINT `{constraintName}` FOREIGN KEY ({foreignColumns}) REFERENCES `{primaryTable}` ({primaryColumns}) ON UPDATE {updateAction} ON DELETE {deleteAction}";

            loggingService.Log($"Creating foreign key {constraintName} on table {foreignTable}");
            try
            {
                using var command = new MySqlCommand(sql, connection);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Failed creating foreign key {constraintName}: {ex.Message}");
                throw;
            }
        }
    }

    public void CreateIndexes(SourceStructureModel sourceStructure)
    {
        if (sourceStructure.IndexNames.Count == 0) return;

        using var connection = new MySqlConnection(configurationService.MySqlConnectionString);
        connection.Open();

        var indexGroups = sourceStructure.IndexColumnInfo.AsEnumerable()
            .GroupBy(row => new { Table = row.Field<string>("tableName"), Index = row.Field<string>("indexName") });

        foreach (var group in indexGroups)
        {
            var tableName = group.Key.Table.ToLower();
            var indexName = group.Key.Index.Length > 64 ? group.Key.Index.Substring(0, 64) : group.Key.Index;
            var isUnique = group.First().Field<bool>("isUnique");
            var columns = string.Join(", ", group.Select(r => $"`{r.Field<string>("columnName")}`"));

            var uniqueClause = isUnique ? "UNIQUE " : string.Empty;
            var sql = $"CREATE {uniqueClause}INDEX `{indexName}` ON `{tableName}` ({columns})";

            loggingService.Log($"Creating index {indexName} on table {tableName}");
            try
            {
                using var command = new MySqlCommand(sql, connection);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Failed creating index {indexName}: {ex.Message}");
                throw;
            }
        }
    }

    public bool CanConnect()
    {
        try
        {
            using var connection = new MySqlConnection(configurationService.MySqlConnectionString);
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
