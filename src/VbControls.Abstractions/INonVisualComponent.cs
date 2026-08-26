namespace VbControls.Abstractions;

/// <summary>
/// Un componente che vive sul Form (nome, Property Grid, campo generato) ma non produce
/// nulla nel DOM a runtime: es. un client HTTP, un timer, un wrapper su LocalStorage
/// (sezione 2.1 di ARCHITECTURE.md — filosofia "tutto vive nel Form", stile C++Builder/
/// Delphi). Nessuno <see cref="StyleModel"/>: a runtime non c'e' nulla da stilare perche'
/// non c'e' nulla da vedere. In fase di design il componente Blazor collante mostra solo
/// un'icona, condizionata al flag `?design=true` (modulo 10).
/// </summary>
public interface INonVisualComponent : IDesignComponent;
