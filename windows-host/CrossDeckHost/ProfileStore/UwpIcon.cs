using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CrossDeckHost.ProfileStore;

/// <summary>
/// UWP/Store apps (AppDiscovery's "uwp:" ExePath prefix) have no .exe for
/// <see cref="System.Drawing.Icon.ExtractAssociatedIcon"/> or <see cref="JumboIcon"/> to read a
/// classic icon resource from — their tile art lives in the package's own asset files instead.
/// <c>IShellItemImageFactory</c> is the same Shell mechanism Explorer/Start use to render a Store
/// app's tile, addressed the same way as launching one (shell:AppsFolder\{AppUserModelId}), so it
/// works uniformly without parsing the package manifest's logo asset ourselves.
/// </summary>
internal static class UwpIcon
{
    private static readonly Guid IID_IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const uint SIIGBF_BIGGERSIZEOK = 0x1;

    /// <summary>Returns the largest available tile bitmap for the given AppUserModelId
    /// ("{familyName}!{appId}", i.e. a "uwp:" ExePath with that prefix stripped), or null if
    /// nothing resolves it. Tries the package's own asset files on disk first — genuine native
    /// pixels, no Shell-side scaling to go soft — falling back to the Shell's AppsFolder tile
    /// image (IShellItemImageFactory) only if no usable asset file is found.</summary>
    public static System.Drawing.Bitmap? ExtractTile(string appUserModelId)
    {
        return ExtractFromPackageAssets(appUserModelId) ?? ExtractFromShell(appUserModelId);
    }

    /// <summary>Reads the package's own logo PNG directly off disk instead of going through any
    /// Shell rendering path. The manifest's Square44x44Logo/Square150x150Logo attributes give the
    /// asset's REAL base filename — apps are free to name it anything ("Assets\AppList.png" for
    /// WhatsApp, not literally "Square44x44Logo.png") — so this has to read the manifest rather
    /// than guess a filename pattern. Once the base name is known, prefers the highest-resolution
    /// "altform-unplated" variant (a transparent, no-background asset well-behaved packages ship
    /// specifically for icon/jumplist use), then the highest-resolution plain variant.</summary>
    private static System.Drawing.Bitmap? ExtractFromPackageAssets(string appUserModelId)
    {
        try
        {
            var familyName = appUserModelId.Split('!')[0];
            var packageManager = new Windows.Management.Deployment.PackageManager();
            var package = packageManager.FindPackagesForUser(string.Empty)
                .FirstOrDefault(p => p.Id.FamilyName == familyName);
            if (package == null) return null;

            var installedPath = package.InstalledPath;
            var manifestPath = Path.Combine(installedPath, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return null;

            var doc = System.Xml.Linq.XDocument.Load(manifestPath);
            System.Xml.Linq.XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
            var visualElements = doc.Descendants(uap + "VisualElements").FirstOrDefault();
            if (visualElements == null) return null;

            var candidates = Directory.GetFiles(installedPath, "*.png", SearchOption.AllDirectories);
            string? best = PickBestAsset(candidates, visualElements.Attribute("Square44x44Logo")?.Value, requireUnplated: true)
                ?? PickBestAsset(candidates, visualElements.Attribute("Square310x310Logo")?.Value, requireUnplated: false)
                ?? PickBestAsset(candidates, visualElements.Attribute("Square150x150Logo")?.Value, requireUnplated: false)
                ?? PickBestAsset(candidates, visualElements.Attribute("Square44x44Logo")?.Value, requireUnplated: false);
            if (best == null) return null;

            using var fileBytes = new MemoryStream(File.ReadAllBytes(best));
            return new System.Drawing.Bitmap(fileBytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>logoDeclaredPath is the manifest's raw attribute value, e.g. "Assets\AppList.png"
    /// — only its base filename ("AppList") is real; the actual variant files on disk are named
    /// "AppList.targetsize-256_altform-unplated.png" etc., not the literal declared path.</summary>
    private static string? PickBestAsset(string[] candidates, string? logoDeclaredPath, bool requireUnplated)
    {
        if (string.IsNullOrEmpty(logoDeclaredPath)) return null;
        var baseName = Path.GetFileNameWithoutExtension(logoDeclaredPath);

        var matches = candidates.Where(f =>
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (!name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)) return false;
            // "altform-lightunplated" is a distinct theme variant, not a match for plain
            // "altform-unplated" — an exact substring check keeps the two from tying against
            // each other when both exist at the same targetsize.
            var isUnplated = name.Contains("altform-unplated", StringComparison.OrdinalIgnoreCase);
            return requireUnplated ? isUnplated : !isUnplated;
        }).ToList();
        if (matches.Count == 0) return null;

        // Prefer the highest "targetsize-N" or "scale-N" suffix so a 256px asset wins over a 44px
        // one instead of whichever the filesystem happens to enumerate first.
        return matches
            .Select(f => (Path: f, Size: ParseAssetSize(Path.GetFileName(f))))
            .OrderByDescending(x => x.Size)
            .First().Path;
    }

    private static int ParseAssetSize(string fileName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(?:targetsize|scale)-(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static System.Drawing.Bitmap? ExtractFromShell(string appUserModelId)
    {
        var riid = IID_IShellItemImageFactory;
        if (SHCreateItemFromParsingName("shell:AppsFolder\\" + appUserModelId, IntPtr.Zero, ref riid, out var factory) != 0)
            return null;

        var hbm = IntPtr.Zero;
        try
        {
            factory.GetImage(new SIZE { cx = 512, cy = 512 }, SIIGBF_BIGGERSIZEOK, out hbm);
            if (hbm == IntPtr.Zero) return null;
            using var raw = System.Drawing.Image.FromHbitmap(hbm);
            return new System.Drawing.Bitmap(raw);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
            Marshal.ReleaseComObject(factory);
        }
    }
}
