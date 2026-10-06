using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MonitorCloud.TestShared;

/// <summary>Deletes all rows with a script generated from the EF model, dependants first.</summary>
public static class TestDatabase
{
    public static async Task ResetAsync(DbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        var script = BuildDeleteScript(db.Model);
        if (script.Length > 0)
            await db.Database.ExecuteSqlRawAsync(script);
    }

    public static string BuildDeleteScript(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var tables = model.GetEntityTypes()
            .Where(t => t.GetTableName() is not null && !t.IsOwned())
            .GroupBy(t => (Schema: t.GetSchema() ?? "dbo", Table: t.GetTableName()!))
            .ToDictionary(g => g.Key, g => g.SelectMany(t => t.GetForeignKeys())
                .Select(fk => (Schema: fk.PrincipalEntityType.GetSchema() ?? "dbo", Table: fk.PrincipalEntityType.GetTableName()!))
                .Where(p => p != g.Key)
                .ToHashSet());

        // Topological order: a table is deleted before the tables it references.
        var ordered = new List<(string Schema, string Table)>();
        var visited = new HashSet<(string, string)>();
        void Visit((string Schema, string Table) table)
        {
            if (!visited.Add(table))
                return;
            foreach (var dependant in tables.Where(t => t.Value.Contains(table)).Select(t => t.Key))
                Visit(dependant);
            ordered.Add(table);
        }

        foreach (var table in tables.Keys.OrderBy(t => t.Schema, StringComparer.Ordinal).ThenBy(t => t.Table, StringComparer.Ordinal))
            Visit(table);

        return string.Join(Environment.NewLine, ordered.Select(t => $"DELETE FROM [{t.Schema}].[{t.Table}];"));
    }
}
