namespace PbiBench.Core.Commands;

public enum WorkbenchCommandId
{
    Open, Connect, Save, Undo, Redo, RunBpa, Automate, DaxStudio, Diagram,
    Scripts, Dependencies, FormatDax, NewModel
}

/// <summary>Instance-owned command routes shared by shell controls, shortcuts and editor adapters.</summary>
public sealed class WorkbenchCommandRegistry
{
    private readonly Dictionary<WorkbenchCommandId, Entry> entries = new();

    public void Register(WorkbenchCommandId id, Action execute, Func<bool>? canExecute = null)
    {
        if (execute == null) throw new ArgumentNullException(nameof(execute));
        entries[id] = new Entry(execute, canExecute ?? (() => true));
    }

    public bool Contains(WorkbenchCommandId id) => entries.ContainsKey(id);
    public bool CanExecute(WorkbenchCommandId id) => entries.TryGetValue(id, out var entry) && entry.CanExecute();

    public bool Execute(WorkbenchCommandId id)
    {
        if (!entries.TryGetValue(id, out var entry) || !entry.CanExecute()) return false;
        entry.Execute();
        return true;
    }

    private sealed record Entry(Action Execute, Func<bool> CanExecute);
}
