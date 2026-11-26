namespace Orbit.Core.Formats;

public sealed record FormatCategory(
    string Id,
    string Title,
    string Accent,
    string OnAccent,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Targets,
    IReadOnlyDictionary<string, string> Hints)
{
    public IReadOnlyList<string> FolderTargets { get; init; } = [];

    public IReadOnlyList<string> StreamCopyGroup { get; init; } = [];
}
