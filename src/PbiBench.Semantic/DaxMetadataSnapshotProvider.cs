using PbiBench.Dax.LanguageService;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic;

/// <summary>Capture on the model-owning UI thread, then pass immutable values to background DAX analysis.</summary>
public static class DaxMetadataSnapshotProvider
{
    public static DaxMetadataSnapshot Capture(TabularModelHandler handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var symbols = new List<DaxSymbol>();
        foreach (var table in handler.Model.Tables)
        {
            symbols.Add(Symbol(table, DaxSymbolKind.Table));
            symbols.AddRange(table.Columns.Select(column => Symbol(column, DaxSymbolKind.Column, table.Name, column.DataType.ToString())));
            symbols.AddRange(table.Measures.Select(measure => Symbol(measure, DaxSymbolKind.Measure, table.Name, measure.DataType.ToString())));
            if (table is CalculationGroupTable group)
                symbols.AddRange(group.CalculationItems.Select(item => Symbol(item, DaxSymbolKind.CalculationItem, table.Name)));
        }
        symbols.AddRange(handler.Model.Functions.Select(function => Symbol(function, DaxSymbolKind.Function)));
        return new DaxMetadataSnapshot(symbols, handler.CompatibilityLevel);
    }

    public static TabularNamedObject? Resolve(TabularModelHandler handler, DaxSymbol symbol)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (symbol == null) throw new ArgumentNullException(nameof(symbol));
        if (symbol.Kind == DaxSymbolKind.Function)
            return handler.Model.Functions.FirstOrDefault(item => string.Equals(item.Name, symbol.Name, StringComparison.OrdinalIgnoreCase));
        var tableName = symbol.Kind == DaxSymbolKind.Table ? symbol.Name : symbol.Table;
        var table = handler.Model.Tables.FirstOrDefault(item => string.Equals(item.Name, tableName, StringComparison.OrdinalIgnoreCase));
        return symbol.Kind switch
        {
            DaxSymbolKind.Table => table,
            DaxSymbolKind.Column => table?.Columns.FirstOrDefault(item => string.Equals(item.Name, symbol.Name, StringComparison.OrdinalIgnoreCase)),
            DaxSymbolKind.Measure => table?.Measures.FirstOrDefault(item => string.Equals(item.Name, symbol.Name, StringComparison.OrdinalIgnoreCase)),
            DaxSymbolKind.CalculationItem => (table as CalculationGroupTable)?.CalculationItems.FirstOrDefault(item => string.Equals(item.Name, symbol.Name, StringComparison.OrdinalIgnoreCase)),
            _ => null
        };
    }

    public static TabularNamedObject? Resolve(TabularModelHandler handler, DaxSymbolLocation location)
    {
        if (location == null) throw new ArgumentNullException(nameof(location));
        var symbol = Capture(handler).Symbols.FirstOrDefault(item => item.Id == location.SymbolId);
        return symbol == null ? null : Resolve(handler, symbol);
    }

    public static TabularNamedObject? Resolve(TabularModelHandler handler, string symbolId)
    {
        var symbol = Capture(handler).Symbols.FirstOrDefault(item => item.Id == symbolId);
        return symbol == null ? null : Resolve(handler, symbol);
    }

    private static DaxSymbol Symbol(TabularNamedObject item, DaxSymbolKind kind, string? table = null, string? dataType = null) =>
        new($"{kind}:{SemanticModelService.ObjectPath(item)}", item.Name, kind, table,
            (item as IExpressionObject)?.Expression, (item as IDescriptionObject)?.Description, dataType,
            (item as IHideableObject)?.IsHidden ?? false);
}
