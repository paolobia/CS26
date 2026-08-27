using System.Text.Json;
using VbControls.Abstractions;

namespace Ide.Designer;

/// <summary>
/// Modulo 17: Undo/Redo per la superficie di design, a snapshot (non un vero command
/// pattern): ogni operazione che modifica <c>_placedControls</c> (piazzamento, cancellazione,
/// riordino, modifica di una proprieta') registra un'istantanea JSON dell'intera lista PRIMA
/// della modifica. Piu' semplice e piu' robusto di tracciare ogni singola operazione come un
/// comando reversibile - il costo (serializzare l'intera lista ad ogni modifica) e'
/// trascurabile per il numero di controlli che un Form di questo IDE avra' mai.
/// Fuori scope esplicito: le modifiche al codice in {Form}.Behavior.cs (corpi dei metodi)
/// non passano da qui - sono testo libero in un file a parte, non fanno parte dello stato
/// serializzabile di <see cref="PlacedControl"/>.
/// </summary>
public sealed class UndoRedoManager
{
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    // Chiamato PRIMA di ogni modifica a _placedControls, con lo stato ancora vecchio.
    public void RecordSnapshot(IReadOnlyList<PlacedControl> currentState)
    {
        _undoStack.Push(Serialize(currentState));
        _redoStack.Clear(); // una nuova modifica invalida sempre la cronologia "avanti"
    }

    public List<PlacedControlState>? Undo(IReadOnlyList<PlacedControl> currentState)
    {
        if (_undoStack.Count == 0)
            return null;

        _redoStack.Push(Serialize(currentState));
        return Deserialize(_undoStack.Pop());
    }

    public List<PlacedControlState>? Redo(IReadOnlyList<PlacedControl> currentState)
    {
        if (_redoStack.Count == 0)
            return null;

        _undoStack.Push(Serialize(currentState));
        return Deserialize(_redoStack.Pop());
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private static string Serialize(IReadOnlyList<PlacedControl> controls) =>
        JsonSerializer.Serialize(controls.Select(ProjectStateStore.ToState).ToList());

    private static List<PlacedControlState> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<PlacedControlState>>(json) ?? [];
}
