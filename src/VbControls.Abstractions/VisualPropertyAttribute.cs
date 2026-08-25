namespace VbControls.Abstractions;

/// <summary>
/// Marca una proprieta' pubblica come editabile dalla Property Grid del designer
/// (modulo 8 di ARCHITECTURE.md), raggruppata per <see cref="Category"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class VisualPropertyAttribute : Attribute
{
    public VisualPropertyAttribute(string category)
    {
        Category = category;
    }

    /// <summary>Categoria di raggruppamento nella Property Grid (es. "Layout", "Aspetto").</summary>
    public string Category { get; }
}
