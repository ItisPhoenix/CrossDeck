using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CrossDeckHost;

/// <summary>One installed app found via Start Menu shortcut enumeration.</summary>
public record DiscoveredApp(string Name, string ExePath)
{
    /// <summary>Populated on first use by the picker UI, not during discovery — extracting an
    /// icon for every installed app upfront would stutter on a machine with 100+ apps.</summary>
    public System.Windows.Media.ImageSource? Icon { get; set; }
}

/// <summary>
/// Enumerates installed Windows apps from Start Menu shortcuts (all-users + per-user), feeding the
/// Application Path combo box in EditorWindow (an editable ComboBox — pick from the list, or type/
/// browse a custom path). Deliberately Start-Menu-only for v1 —
/// covers the vast majority of installed apps with the smallest, most reliable implementation (no
/// registry Uninstall-key parsing, no UWP/Store package enumeration — see MASTER-PLAN.md decision
/// log for why those were deferred).
/// </summary>
public static class AppDiscovery
{
    private static readonly Dictionary<string, System.Windows.Media.ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Extracts and caches a small icon for an app row. UWP apps (uwp: prefix) aren't
    /// covered here — skipped, not worth the extra Shell API surface for a picker thumbnail.</summary>
    public static System.Windows.Media.ImageSource? GetOrLoadIcon(string exePath)
    {
        if (_iconCache.TryGetValue(exePath, out var cached)) return cached;
        if (exePath.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase))
        {
            _iconCache[exePath] = null;
            return null;
        }

        System.Windows.Media.ImageSource? result = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon != null)
            {
                result = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                result.Freeze();
            }
        }
        catch { /* not every path resolves — row just shows no icon */ }

        _iconCache[exePath] = result;
        return result;
    }

    // Start Menu clutter that isn't something you'd want to assign to a deck button.
    private static readonly string[] ExcludeNameSubstrings =
    {
        "uninstall", "readme", "read me", "help", "license", "changelog",
        "release notes", "documentation", "website", "web site"
    };

    public static List<DiscoveredApp> DiscoverApps()
    {
        // WScript.Shell (used below to resolve .lnk targets) is an STA-only COM object. Called
        // from a thread-pool/MTA thread — e.g. the Android app-list WebSocket handler, which runs
        // this via Task.Run — every shortcut resolution silently throws and returns null via
        // ResolveShortcutTarget's catch-all, making almost every real app vanish with no visible
        // error. The WPF UI thread is already STA (callers from there hit the fast path below).
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return DiscoverAppsCore();

        List<DiscoveredApp>? result = null;
        var staThread = new Thread(() => result = DiscoverAppsCore());
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();
        return result ?? new List<DiscoveredApp>();
    }

    private static List<DiscoveredApp> DiscoverAppsCore()
    {
        // Dedupe by exe path — the same app often has multiple shortcuts (Start Menu + desktop
        // folder copies under different display names).
        var results = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var startMenuDir in GetStartMenuDirs())
        {
            if (!Directory.Exists(startMenuDir)) continue;

            IEnumerable<string> lnkFiles;
            try
            {
                lnkFiles = Directory.EnumerateFiles(startMenuDir, "*.lnk", SearchOption.AllDirectories);
            }
            catch
            {
                continue; // permission errors on odd system folders — skip that root, not the whole scan
            }

            foreach (var lnkPath in lnkFiles)
            {
                var name = Path.GetFileNameWithoutExtension(lnkPath);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (ExcludeNameSubstrings.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;

                var targetPath = ResolveShortcutTarget(lnkPath);
                if (string.IsNullOrWhiteSpace(targetPath)) continue;
                if (!targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(targetPath)) continue;

                results.TryAdd(targetPath, new DiscoveredApp(name, targetPath));
            }
        }

        // Add standard Windows system utilities if they exist
        var windir = Environment.GetEnvironmentVariable("windir") ?? @"C:\Windows";
        var system32 = Path.Combine(windir, "System32");
        var commonUtilities = new List<(string Name, string ExePath)>
        {
            ("Notepad", Path.Combine(system32, "notepad.exe")),
            ("Notepad", Path.Combine(windir, "notepad.exe")),
            ("Command Prompt", Path.Combine(system32, "cmd.exe")),
            ("Calculator", Path.Combine(system32, "calc.exe")),
            ("Paint", Path.Combine(system32, "mspaint.exe")),
            ("Task Manager", Path.Combine(system32, "taskmgr.exe")),
            ("PowerShell", Path.Combine(system32, @"WindowsPowerShell\v1.0\powershell.exe")),
            ("Snipping Tool", Path.Combine(system32, "snippingtool.exe")),
            ("File Explorer", Path.Combine(windir, "explorer.exe")),
            ("Control Panel", Path.Combine(system32, "control.exe")),
            ("WordPad", Path.Combine(system32, "write.exe"))
        };

        foreach (var util in commonUtilities)
        {
            if (File.Exists(util.ExePath))
            {
                results.TryAdd(util.ExePath, new DiscoveredApp(util.Name, util.ExePath));
            }
        }

        foreach (var uwpApp in DiscoverUwpApps())
        {
            results.TryAdd(uwpApp.ExePath, uwpApp);
        }

        return results.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Enumerates Store/UWP apps registered for the current user via the Package Manager API.
    /// These have no plain .exe to launch (Process.Start doesn't work on them) — represented
    /// with a "uwp:" prefix on ExePath so ActionExecutor can launch them via shell:AppsFolder
    /// instead. Framework/runtime packages (no visible entry point) are skipped.
    /// </summary>
    private static IEnumerable<DiscoveredApp> DiscoverUwpApps()
    {
        var results = new List<DiscoveredApp>();
        try
        {
            var packageManager = new Windows.Management.Deployment.PackageManager();
            var packages = packageManager.FindPackagesForUser(string.Empty);

            foreach (var package in packages)
            {
                if (package.IsFramework || package.IsResourcePackage) continue;

                string familyName;
                string displayName;
                try
                {
                    familyName = package.Id.FamilyName;
                    displayName = package.DisplayName;
                }
                catch
                {
                    continue; // some system packages throw reading these properties
                }

                if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(familyName)) continue;

                string? appId = null;
                try
                {
                    if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) continue;
                    var installedPath = package.InstalledPath;
                    var manifestPath = System.IO.Path.Combine(installedPath, "AppxManifest.xml");
                    if (System.IO.File.Exists(manifestPath))
                    {
                        var doc = System.Xml.Linq.XDocument.Load(manifestPath);
                        var ns = doc.Root?.GetDefaultNamespace() ?? System.Xml.Linq.XNamespace.None;
                        appId = doc.Descendants(ns + "Application").FirstOrDefault()?.Attribute("Id")?.Value;
                    }
                }
                catch
                {
                    // Manifest read failures just mean we skip this package below.
                }

                if (string.IsNullOrWhiteSpace(appId)) continue;

                results.Add(new DiscoveredApp(displayName, $"uwp:{familyName}!{appId}"));
            }
        }
        catch
        {
            // PackageManager unsupported/unavailable on this Windows build — return what we have.
        }
        return results;
    }

    private static IEnumerable<string> GetStartMenuDirs()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        // Some installers only drop a Desktop shortcut, no Start Menu entry — catch those too.
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    /// <summary>
    /// Resolves a .lnk file's target path via the Windows Script Host shell automation object
    /// (WScript.Shell) — built into every Windows install, so this needs no new NuGet dependency
    /// and no hand-rolled MS-SHLLINK binary parser (which has real edge cases: Unicode vs ANSI
    /// paths, environment-variable expansion, network paths — WshShortcut.TargetPath already
    /// handles all of that correctly). Late-bound via reflection (Type.InvokeMember) rather than
    /// the `dynamic` keyword, so this doesn't need the Microsoft.CSharp package either.
    /// </summary>
    private static string? ResolveShortcutTarget(string lnkPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            shortcut = shellType.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            if (shortcut == null) return null;

            var targetPath = shortcut.GetType().InvokeMember("TargetPath",
                System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;

            if (string.IsNullOrWhiteSpace(targetPath)) return null;

            if (Path.GetFileName(targetPath).Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ResolveSquirrelRealExe(targetPath, shortcut);
                if (resolved != null) return resolved;
            }

            return targetPath;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shortcut != null) Marshal.ReleaseComObject(shortcut);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>Squirrel apps (Discord, Slack, Claude Desktop, etc.) shortcut to Update.exe with
    /// "--processStart AppName.exe" args, not the real exe — so icon extraction hits the generic
    /// updater stub. The real exe lives under Update.exe's sibling "app-&lt;version&gt;" folder.</summary>
    private static string? ResolveSquirrelRealExe(string updateExePath, object shortcut)
    {
        try
        {
            var arguments = shortcut.GetType().InvokeMember("Arguments",
                System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
            if (string.IsNullOrWhiteSpace(arguments)) return null;

            var match = System.Text.RegularExpressions.Regex.Match(arguments, "--processStart\\s+\"?([^\"\\s]+\\.exe)\"?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            var realExeName = match.Groups[1].Value;

            var installDir = Path.GetDirectoryName(updateExePath);
            if (installDir == null) return null;

            var appFolders = Directory.GetDirectories(installDir, "app-*")
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var folder in appFolders)
            {
                var candidate = Path.Combine(folder, realExeName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch
        {
            // Any failure here just means we fall back to the Update.exe stub icon/path.
        }
        return null;
    }
}
