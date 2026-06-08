# OpenDbConvert

A .NET 8 command-line tool that migrates a SQL Server database to MySQL. It reads the source schema and data from SQL Server, recreates the structure in MySQL, and imports all table data including foreign keys and indexes.

## Features

- Converts SQL Server tables, columns, primary keys, foreign keys, and indexes to MySQL equivalents
- Imports all table data with transaction-safe row-by-row inserts
- Clears the destination database before each run to ensure a clean migration
- Supports pre-process SQL scripts executed against SQL Server before migration begins
- Supports post-process SQL scripts executed against MySQL after migration completes
- Logs all activity to the console and optionally to a file
- Validates connectivity to both databases before starting

## Requirements

- .NET 8 SDK or Runtime
- SQL Server (source database)
- MySQL (destination database)

## Configuration

All configuration is stored in `config.json`, which must be present in the same directory as the executable.

```json
{
  "SqlServerConnectionString": "Data Source=localhost;Initial Catalog=your_db;User ID=sa;Password=your_password;TrustServerCertificate=true",
  "MySqlConnectionString": "Server=localhost;Database=your_db;Uid=root;Password=your_password",
  "PreProcessSqlDirectory": "PreSql",
  "PostProcessSqlDirectory": "PostSql",
  "LogOutputFile": "dbconvert.log"
}
```

| Field | Description |
|---|---|
| `SqlServerConnectionString` | ADO.NET connection string for the SQL Server source database |
| `MySqlConnectionString` | ADO.NET connection string for the MySQL destination database |
| `PreProcessSqlDirectory` | Directory containing `.sql` files to execute on SQL Server before migration. Leave empty to skip. |
| `PostProcessSqlDirectory` | Directory containing `.sql` files to execute on MySQL after migration. Leave empty to skip. |
| `LogOutputFile` | Path to the log file. Leave empty to disable file logging. |

## Pre/Post Process Scripts

Scripts placed in the configured directories are executed in alphabetical order by filename. Use a numeric prefix to control execution order.

**Example:**
```
PreSql/
  01_cleanup_temp_data.sql
  02_normalize_values.sql

PostSql/
  01_create_views.sql
  02_update_sequences.sql
```

Pre-process scripts run against the SQL Server source database. Post-process scripts run against the MySQL destination database.

## Data Type Mapping

OpenDbConvert automatically converts SQL Server data types, defaults, and other schema elements to their MySQL equivalents.

For the current mapping definitions, see:
- [ConversionMap.cs](OpenDbConvert/Abstractions/ConversionMap.cs)

The source file is the authoritative reference and may be updated as compatibility improvements are added.

## Migration Process

The tool executes the following steps in order:

1. Load and validate configuration
2. Verify connectivity to SQL Server and MySQL
3. Execute pre-process scripts against SQL Server
4. Read all table names, foreign key definitions, and index definitions from SQL Server
5. Drop all foreign keys and tables from the MySQL destination database
6. Create tables in MySQL
7. Import all row data into each table
8. Create foreign keys in MySQL
9. Create indexes in MySQL
10. Execute post-process scripts against MySQL

## Usage

### Linux / macOS

```bash
./OpenDbConvert
```

### Windows

```powershell
OpenDbConvert.exe
```

The tool exits with code `1` if configuration is invalid or either database connection fails. All errors are written to stderr and to the log file if configured.

## Dependencies

| Package | Version |
|---|---|
| `Microsoft.Data.SqlClient` | 5.2.2 |
| `Microsoft.SqlServer.SqlManagementObjects` | 172.87.0 |
| `MySql.Data` | 9.3.0 |
| `Microsoft.Extensions.DependencyInjection` | 8.0.1 |

## License

This project is licensed under the **GNU Lesser General Public License v3.0 (LGPL-3.0)**.

You are free to use, modify, and distribute this software, including in proprietary
projects, provided that:

- Any modifications to this library itself are released under the same LGPL-3.0 license
- Appropriate credit is given to the original author(s)
- A copy of the license or a link to it is included with any distribution

This software is provided **as-is**, without warranty of any kind. The authors are not
liable for any damages or issues arising from its use.

See the [LICENSE](LICENSE) file for full details
