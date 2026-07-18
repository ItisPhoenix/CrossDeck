using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CrossDeckHost;

/// <summary>One installed app found via Start Menu shortcut enumeration.</summary>
public record DiscoveredApp(string Name, string ExePath);

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

        return results.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
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

            return string.IsNullOrWhiteSpace(targetPath) ? null : targetPath;
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
}
