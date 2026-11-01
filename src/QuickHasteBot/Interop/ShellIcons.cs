using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>
/// Explorer-Icons für Dateien und Ordner (ersetzt Etier.IconHelper der alten Version).
/// Gecacht pro Extension; .url/.lnk/.exe haben eigene Icons und werden pro Datei geholt.
/// </summary>
internal static class ShellIcons
{
    static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase) { ".url", ".lnk", ".exe", ".ico" };
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(Snippet snippet)
    {
        if (snippet.IsFolder)
            return Get("<folder>", "folder", FILE_ATTRIBUTE_DIRECTORY, useAttributes: true);

        var extension = Path.GetExtension(snippet.FullPath);
        return PerFileExtensions.Contains(extension)
            ? Get(snippet.FullPath, snippet.FullPath, FILE_ATTRIBUTE_NORMAL, useAttributes: false)
            : Get(extension, "file" + extension, FILE_ATTRIBUTE_NORMAL, useAttributes: true);
    }

    static ImageSource? Get(string cacheKey, string path, uint attributes, bool useAttributes)
    {
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

        ImageSource? image = null;
        var info = new SHFILEINFO();
        // Große Icons (32 px) herunterskaliert sehen bei 150 % Skalierung besser aus als die kleinen
        var flags = SHGFI_ICON | SHGFI_LARGEICON | (useAttributes ? SHGFI_USEFILEATTRIBUTES : 0);
        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) != 0 && info.hIcon != 0)
        {
            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                image = bitmap;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }

        Cache[cacheKey] = image;
        return image;
    }
}
