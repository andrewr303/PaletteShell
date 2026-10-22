using System;
using System.IO;

namespace PaletteShellExtension.Tests;

/// <summary>A temporary directory tree for a test, deleted on dispose.</summary>
internal sealed class TestFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("pssession-").FullName;

    /// <summary>Creates (and returns the full path of) a subfolder; nested with '/'.</summary>
    public string Create(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
