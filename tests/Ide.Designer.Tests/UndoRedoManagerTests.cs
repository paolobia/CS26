using Ide.Designer;
using VbControls;
using Xunit;

namespace Ide.Designer.Tests;

public class UndoRedoManagerTests
{
    [Fact]
    public void Senza_snapshot_registrati_CanUndo_e_falso()
    {
        var manager = new UndoRedoManager();
        Assert.False(manager.CanUndo);
        Assert.Null(manager.Undo([]));
    }

    [Fact]
    public void Undo_ripristina_lo_stato_precedente_alla_modifica()
    {
        var manager = new UndoRedoManager();
        var before = new List<PlacedControl> { new("Button1", "VbButton", new VbButtonVisual()) };

        manager.RecordSnapshot(before);
        var after = new List<PlacedControl>(); // simula la cancellazione del controllo

        var restored = manager.Undo(after);

        Assert.NotNull(restored);
        Assert.Single(restored!);
        Assert.Equal("Button1", restored![0].FieldName);
    }

    [Fact]
    public void Redo_dopo_Undo_ripristina_lo_stato_annullato()
    {
        var manager = new UndoRedoManager();
        var before = new List<PlacedControl> { new("Button1", "VbButton", new VbButtonVisual()) };
        manager.RecordSnapshot(before);
        var after = new List<PlacedControl>();

        manager.Undo(after); // ora CanRedo e' vero, "after" (vuoto) e' salvato per il redo
        var redone = manager.Redo(before); // "before" qui rappresenta lo stato attualmente ripristinato (con Button1)

        Assert.NotNull(redone);
        Assert.Empty(redone!);
    }

    [Fact]
    public void Una_nuova_modifica_dopo_Undo_invalida_il_Redo()
    {
        var manager = new UndoRedoManager();
        manager.RecordSnapshot([new("Button1", "VbButton", new VbButtonVisual())]);
        manager.Undo([]);
        Assert.True(manager.CanRedo);

        manager.RecordSnapshot([]); // una nuova modifica, non un redo

        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void CanUndo_e_CanRedo_riflettono_lo_stato_delle_pile()
    {
        var manager = new UndoRedoManager();
        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);

        manager.RecordSnapshot([]);
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);

        manager.Undo([]);
        Assert.False(manager.CanUndo);
        Assert.True(manager.CanRedo);
    }

    [Fact]
    public void Clear_svuota_entrambe_le_pile()
    {
        var manager = new UndoRedoManager();
        manager.RecordSnapshot([new("Button1", "VbButton", new VbButtonVisual())]);
        manager.Undo([]);

        manager.Clear();

        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }
}
