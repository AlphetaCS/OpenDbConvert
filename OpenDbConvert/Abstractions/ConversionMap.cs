using System.Collections.Generic;

namespace OpenDbConvert.Abstractions;

internal static class ConversionMap
{
    internal static readonly Dictionary<string, string> Map = new()
    {
        { "int",              "int" },
        { "bigint",           "bigint" },
        { "smallint",         "smallint" },
        { "tinyint",          "tinyint" },
        { "bit",              "boolean" },
        { "float",            "double" },
        { "real",             "float" },
        { "datetime",         "datetime" },
        { "datetime2",        "datetime" },
        { "smalldatetime",    "datetime" },
        { "date",             "date" },
        { "time",             "time" },
        { "uniqueidentifier", "char(36)" },
        { "text",             "longtext" },
        { "ntext",            "longtext" },
        { "image",            "longblob" },
        { "xml",              "longtext" },
        { "money",            "decimal(19,4)" },
        { "smallmoney",       "decimal(10,4)" }
    };
}
