using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Thin wrapper over the OS-native file/directory picker (<see cref="DisplayServer.FileDialogShow(string,string,string,bool,DisplayServer.FileDialogMode,string[],Callable)"/>).
/// The picked path is an absolute filesystem path anywhere the process can reach — not confined to a
/// configured asset source. The callback fires on the main thread; <paramref name="onPick"/> gets the
/// chosen path, or null if the dialog was cancelled or the platform has no native dialog.
/// </summary>
public static class NativeFileDialog
{
    public static bool IsSupported => DisplayServer.HasFeature(DisplayServer.Feature.NativeDialogFile);

    /// <summary>Opens a single-file picker. <paramref name="filters"/> are Godot filter strings, e.g.
    /// <c>"*.png,*.exr ; Images"</c>.</summary>
    public static void PickFile(string title, string[] filters, string startDirectory, Action<string?> onPick) =>
        Show(title, startDirectory, DisplayServer.FileDialogMode.OpenFile, filters, onPick);

    /// <summary>Opens a directory picker.</summary>
    public static void PickDirectory(string title, string startDirectory, Action<string?> onPick) =>
        Show(title, startDirectory, DisplayServer.FileDialogMode.OpenDir, [], onPick);

    private static void Show(string title, string startDirectory, DisplayServer.FileDialogMode mode, string[] filters, Action<string?> onPick)
    {
        if (!IsSupported)
        {
            GD.PushWarning("[NativeFileDialog] No native file dialog on this platform.");
            onPick(null);
            return;
        }

        string start = string.IsNullOrEmpty(startDirectory) ? OS.GetUserDataDir() : startDirectory;
        Error error = DisplayServer.FileDialogShow(
            title,
            start,
            "",
            showHidden: false,
            mode,
            filters,
            Callable.From((bool status, string[] selectedPaths, long _) =>
                onPick(status && selectedPaths.Length > 0 ? selectedPaths[0] : null)));

        if (error != Error.Ok)
        {
            GD.PushWarning($"[NativeFileDialog] FileDialogShow failed: {error}");
            onPick(null);
        }
    }
}
