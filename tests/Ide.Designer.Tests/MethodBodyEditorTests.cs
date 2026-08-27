using Ide.Designer;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Ide.Designer.Tests;

public class MethodBodyEditorTests : IDisposable
{
    private readonly string _pagesDirectory = Path.Combine(Path.GetTempPath(), "MethodBodyEditorTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_pagesDirectory))
            Directory.Delete(_pagesDirectory, recursive: true);
    }

    [Fact]
    public void ReadMethod_file_inesistente_ritorna_non_esiste()
    {
        var behaviorPath = Path.Combine(_pagesDirectory, "TestForm.Behavior.cs");

        var result = MethodBodyEditor.ReadMethod(behaviorPath, "Button1_Click");

        Assert.False(result.Exists);
        Assert.Equal(0, result.LineCount);
    }

    [Fact]
    public void ReadMethod_metodo_inesistente_in_file_esistente_ritorna_non_esiste()
    {
        MethodBodyEditor.WriteMethodBody(_pagesDirectory, "TestForm", "Test.Pages", "Button1_Click", isAsync: false, newBody: "");
        var behaviorPath = Path.Combine(_pagesDirectory, "TestForm.Behavior.cs");

        var result = MethodBodyEditor.ReadMethod(behaviorPath, "Label1_Click");

        Assert.False(result.Exists);
    }

    [Fact]
    public void WriteMethodBody_crea_il_metodo_se_manca_e_ReadMethod_lo_ritrova()
    {
        MethodBodyEditor.WriteMethodBody(_pagesDirectory, "TestForm", "Test.Pages", "Button1_Click", isAsync: false, newBody: "Text = \"ciao\";");
        var behaviorPath = Path.Combine(_pagesDirectory, "TestForm.Behavior.cs");

        var result = MethodBodyEditor.ReadMethod(behaviorPath, "Button1_Click");

        Assert.True(result.Exists);
        Assert.Contains("ciao", result.Body);
        Assert.Equal(1, result.LineCount);
    }

    [Fact]
    public void WriteMethodBody_sostituisce_solo_il_corpo_di_un_metodo_esistente_round_trip()
    {
        var behaviorPath = Path.Combine(_pagesDirectory, "TestForm.Behavior.cs");
        MethodBodyEditor.WriteMethodBody(_pagesDirectory, "TestForm", "Test.Pages", "Button1_Click", isAsync: false, newBody: "int x = 1;");
        MethodBodyEditor.WriteMethodBody(_pagesDirectory, "TestForm", "Test.Pages", "Button1_Click", isAsync: false, newBody: "int x = 2;\nint y = 3;");

        var result = MethodBodyEditor.ReadMethod(behaviorPath, "Button1_Click");

        Assert.True(result.Exists);
        Assert.Contains("x = 2", result.Body);
        Assert.Contains("y = 3", result.Body);
        Assert.DoesNotContain("x = 1", result.Body);
        Assert.Equal(2, result.LineCount);
    }

    [Fact]
    public void WriteMethodBody_non_si_confonde_con_graffe_annidate_o_stringhe_con_graffa()
    {
        var behaviorPath = Path.Combine(_pagesDirectory, "TestForm.Behavior.cs");
        var body = "if (true)\n{\n    var s = \"a}b\";\n}";

        MethodBodyEditor.WriteMethodBody(_pagesDirectory, "TestForm", "Test.Pages", "Button1_Click", isAsync: false, newBody: body);
        var result = MethodBodyEditor.ReadMethod(behaviorPath, "Button1_Click");

        Assert.True(result.Exists);
        Assert.Contains("a}b", result.Body);

        // Il file intero deve restare sintatticamente valido: non solo il metodo estratto.
        // Un conteggio naive di '{'/'}' fallirebbe qui apposta (la stringa "a}b" contiene
        // una graffa che non è sintassi) - verificare con Roslyn e' l'unico modo corretto.
        var fullText = File.ReadAllText(behaviorPath);
        var diagnostics = CSharpSyntaxTree.ParseText(fullText).GetDiagnostics();
        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }

    [Fact]
    public void WriteMethodBody_corpo_async_genera_firma_async_task()
    {
        MethodBodyEditor.WriteMethodBody(_pagesDirectory, "TestForm", "Test.Pages", "Timer1_Tick", isAsync: true, newBody: "await Task.Delay(1);");
        var behaviorPath = Path.Combine(_pagesDirectory, "TestForm.Behavior.cs");

        var content = File.ReadAllText(behaviorPath);

        Assert.Contains("private async Task Timer1_Tick()", content);
        Assert.True(MethodBodyEditor.ReadMethod(behaviorPath, "Timer1_Tick").Exists);
    }

    [Fact]
    public void CountBodyLines_corpo_vuoto_conta_zero()
    {
        Assert.Equal(0, MethodBodyEditor.CountBodyLines(""));
        Assert.Equal(0, MethodBodyEditor.CountBodyLines("   \n  \n"));
    }

    [Fact]
    public void WriteMethodBody_non_tocca_altri_metodi_gia_presenti()
    {
        MethodBodyEditor.WriteMethodBody(_pagesDirectory, "TestForm", "Test.Pages", "Button1_Click", isAsync: false, newBody: "int a = 1;");
        MethodBodyEditor.WriteMethodBody(_pagesDirectory, "TestForm", "Test.Pages", "Label1_Click", isAsync: false, newBody: "int b = 2;");

        var behaviorPath = Path.Combine(_pagesDirectory, "TestForm.Behavior.cs");
        var button = MethodBodyEditor.ReadMethod(behaviorPath, "Button1_Click");
        var label = MethodBodyEditor.ReadMethod(behaviorPath, "Label1_Click");

        Assert.Contains("a = 1", button.Body);
        Assert.Contains("b = 2", label.Body);
    }
}
