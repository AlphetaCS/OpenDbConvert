using System.Collections.Generic;
using System.Data;

namespace OpenDbConvert.Models;

public class SourceStructureModel
{
    public List<string> TableNames { get; set; } = [];
    public List<string> ForeignKeyNames { get; set; } = [];
    public List<string> IndexNames { get; set; } = [];
    public DataTable ForeignKeyColumnInfo { get; set; }
    public DataTable IndexColumnInfo { get; set; }
}
