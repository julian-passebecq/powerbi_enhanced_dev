using System.Security.Cryptography;
using System.Text;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic;

/// <summary>Read-only projections over the live TE2 session. All mutation remains in its undo-aware wrapper.</summary>
public sealed class SemanticModelService
{
    private readonly TabularModelHandler handler;
    public SemanticModelService(TabularModelHandler handler) => this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
    public string ModelName => handler.Database.Name;
    public string Source => handler.Source ?? "Unsaved model";
    public bool HasUnsavedChanges => handler.HasUnsavedChanges;
    public string AvailableTableName(string requestedName) => handler.Database.Model.Tables.GetNewName(requestedName);
    public IReadOnlyList<TabularNamedObject> Inventory() => handler.Model.Tables
        .SelectMany(t => new TabularNamedObject[] { t }.Concat(t.Columns).Concat(t.Measures)).ToArray();

    public ModelGraph GetGraph()
    {
        var relationships = handler.Model.Relationships.OfType<SingleColumnRelationship>()
            .Where(r => r.FromColumn != null && r.ToColumn != null)
            .Select(r => new GraphRelationship(r.Name, r.FromColumn.Table.Name, r.FromColumn.Name,
                r.ToColumn.Table.Name, r.ToColumn.Name, r.FromCardinality.ToString(), r.ToCardinality.ToString(),
                r.IsActive, r.CrossFilteringBehavior.ToString())).ToArray();
        var tables = handler.Model.Tables.Select(t => new GraphTable(t.Name, t,
            relationships.Any(r => (r.FromTable == t.Name && r.FromCardinality == "Many") || (r.ToTable == t.Name && r.ToCardinality == "Many")) ? "Fact" :
            relationships.Any(r => (r.ToTable == t.Name && r.ToCardinality == "One") || (r.FromTable == t.Name && r.FromCardinality == "One")) ? "Dimension" : "Table",
            t.Columns.Select(c => c.Name).ToArray(), t.Measures.Count)).ToArray();
        return new ModelGraph(tables, relationships);
    }

    /// <summary>Whole-model fingerprint prevents a plan being applied after any concurrent metadata edit.</summary>
    public string Fingerprint()
    {
        var json = Microsoft.AnalysisServices.Tabular.JsonSerializer.SerializeDatabase(handler.Database);
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(json)));
    }

    public static string ObjectPath(TabularNamedObject obj) => obj is ITabularTableObject child
        ? "'" + child.Table.Name.Replace("'", "''") + "'[" + obj.Name.Replace("]", "]]") + "]"
        : "'" + obj.Name.Replace("'", "''") + "'";
}

public sealed record ModelGraph(IReadOnlyList<GraphTable> Tables, IReadOnlyList<GraphRelationship> Relationships);
public sealed record GraphTable(string Name, Table Object, string Role, IReadOnlyList<string> Columns, int MeasureCount);
public sealed record GraphRelationship(string Name, string FromTable, string FromColumn, string ToTable, string ToColumn,
    string FromCardinality, string ToCardinality, bool IsActive, string FilterDirection);
