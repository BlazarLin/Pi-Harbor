// Created: 2026-09-07
// Function: Build a safe Windows Explorer launch request for one project directory.
// Purpose: Keep shell path handling isolated and directly testable.

using System.Diagnostics;
using System.IO;

namespace PIHarness.App.Presentation;

public static class FolderLauncher
{
    public static ProcessStartInfo CreateStartInfo(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add(Path.GetFullPath(directory));
        return startInfo;
    }
}
