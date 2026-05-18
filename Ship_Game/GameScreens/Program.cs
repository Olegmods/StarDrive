using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Xna.Framework;

namespace Ship_Game;

internal static class Program
{
    public const int GAME_RUN_FAILURE = -1;
    public const int SCREEN_UPDATE_FAILURE = -2;
    public const int UNHANDLED_EXCEPTION = -3;
    public const int NATIVE_DLL_LOAD_FAILURE = -4;
    public const int WIN_VERSION_TOO_OLD = -5;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    static extern int Win32MessageBox(IntPtr hWnd, string text, string caption, uint type);

    // Set by --apply-patch=<version> CLI arg. AutoPatcher's pre-elevation pass
    // (non-elevated download + unzip) writes a PendingPatch.json marker, then
    // relaunches itself elevated with this flag set so the elevated instance can
    // skip download/unzip and jump straight to file moves. See AutoPatcher.cs.
    public static string ResumePatchVersion;

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        GraphicsDeviceManager graphicsMgr = StarDriveGame.Instance?.Graphics;
        if (graphicsMgr != null && graphicsMgr.IsFullScreen)
            graphicsMgr.ToggleFullScreen();

        var ex = e.ExceptionObject as Exception;
        Log.ErrorDialog(ex, "Program.CurrentDomain_UnhandledException", UNHANDLED_EXCEPTION);
    }

    // in case of abnormal termination, run cleanup tasks during process exit
    static void CurrentDomain_ProcessExit(object sender, EventArgs e)
    {
        RunCleanupAndExit(Environment.ExitCode);
    }

    static bool HasRunCleanupTasks;

    public static void RunCleanup()
    {
        if (HasRunCleanupTasks)
            return;
        try
        {
            HasRunCleanupTasks = true;
            Parallel.ClearPool(); // Dispose all thread pool Threads
            Log.Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while trying to exit the process");
        }
        finally
        {
            Log.FlushAllLogs(); // desperate
        }
    }

    public static void RunCleanupAndExit(int exitCode)
    {
        if (HasRunCleanupTasks)
            return;
            
        Log.Write($"RunCleanupAndExit({exitCode})");
        RunCleanup();
        Environment.Exit(exitCode);
    }

    // @return false if Help should be printed to console
    static bool ParseMainArgs(string[] args)
    {
        foreach (string arg in args)
        {
            string[] parts = arg.Split('=');
            string key = parts[0];
            string value = parts.Length > 1 ? parts[1] : "";

            if (key == "--help")
            {
                return false;
            }
            else if (key == "--mod")
            {
                GlobalStats.LoadModInfo(value);
                if (!GlobalStats.HasMod)
                    throw new Exception($"Mod {value} not found. Argument was: {arg}");
            }
            else if (key == "--export-textures")
            {
                GlobalStats.ExportTextures = true;
            }
            else if (key == "--export-meshes")
            {
                GlobalStats.ExportMeshes = value.IsEmpty() ? "obj" : value;
            }
            else if (key == "--generate-hulls")
            {
                GlobalStats.GenerateNewHullFiles = true;
            }
            else if (key == "--generate-ships")
            {
                GlobalStats.GenerateNewShipDesignFiles = true;
            }
            else if (key == "--fix-roles")
            {
                GlobalStats.FixDesignRoleAndCategory = true;
            }
            else if (key.StartsWith("--run-localizer"))
            {
                GlobalStats.RunLocalizer = value.IsEmpty() ? 1 : int.Parse(value);
            }
            else if (key == "--resource-debug")
            {
                GlobalStats.DebugResourceLoading = true;
            }
            else if (key == "--asset-debug")
            {
                GlobalStats.DebugAssetLoading = true;
            }
            else if (key == "--console")
            {
                Log.ShowConsoleWindow();
            }
            else if (key == "--continue")
            {
                GlobalStats.ContinueToGame = true;
            }
            else if (key == "--apply-patch")
            {
                // Internal — set by AutoPatcher when relaunching itself elevated
                // so the new instance knows to resume from a pre-downloaded patch.
                ResumePatchVersion = value;
            }
            else
            {
                Log.Warning($"Unrecognized argument: '{arg}'");
            }
        }
        return true; // all ok
    }

    static void PrintHelp()
    {
        Log.Write("StarDrive BlackBox Command Line Interface (CLI)");
        Log.Write("  --help              Shows this help message");
        Log.Write("  --mod=\"<mod>\"     Load the game with the specified <mod> path, eg: --mod=\"Combined Arms\" ");
        Log.Write("  --export-textures   Exports all texture files as PNG and DDS to game/ExportedTextures");
        Log.Write("  --export-meshes=obj Exports all mesh files and textures, options: fbx obj fbx+obj");
        Log.Write("  --generate-hulls    Generates new .hull files from old XML hulls");
        Log.Write("  --generate-ships    Generates new ship .design files from old XML ships");
        Log.Write("  --fix-roles         Fixes Role and Category for all .design ships");
        Log.Write("  --run-localizer=[0-2] Run localization tool to merge missing translations and generate id-s");
        Log.Write("                        0: disabled  1: generate with YAML NameIds  2: generate with C# NameIds");
        Log.Write("  --resource-debug    Debug logs all resource loading, mainly for Mods to ensure their assets are loaded");
        Log.Write("  --asset-debug       Debug logs all asset load events, useful for analyzing the order of assets being loaded");
        Log.Write("  --console           Enable the Debug Console which mirrors blackbox.log");
        Log.Write("  --continue          After running CLI tasks, continue to game as normal");
    }

    static void PressAnyKey()
    {
        if (Console.IsInputRedirected)
            return;
        Log.Write(ConsoleColor.Gray, "Press any key to continue...");
        Console.ReadKey(false);
    }

    // CLI tasks
    static bool RunInitializationTasks()
    {
        bool runGame = true; // Ok, continue to game

        if (GlobalStats.RunLocalizer > 0)
        {
            Tools.Localization.LocalizationTool.Run(GlobalStats.ModPath, GlobalStats.RunLocalizer);
            runGame = GlobalStats.ContinueToGame;
        }

        if (GlobalStats.ExportTextures)
        {
            ResourceManager.RootContent.RawContent.ExportAllTextures();
            runGame = GlobalStats.ContinueToGame;
        }

        if (GlobalStats.ExportMeshes != null)
        {
            Log.Write($"ExportMeshes {GlobalStats.ExportMeshes}");
            string[] formats = GlobalStats.ExportMeshes.Split('+'); // "fbx+obj"
            foreach (string ext in formats)
            {
                ResourceManager.RootContent.RawContent.ExportAllXnbMeshes(ext);
            }
            runGame = GlobalStats.ContinueToGame;
        }

        if (!runGame && Log.HasDebugger)
        {
            PressAnyKey();
        }
        return runGame;
    }

    // Probe SDNative.dll explicitly so we can produce a clear, actionable error if the file
    // is blocked by Windows security policy (Smart App Control, WDAC, AppLocker) or quarantined
    // by AV. Without this probe the failure surfaces deep in startup as a TypeInitializationException
    // for Ship_Game.Parallel (the first P/Invoke into SDNative.dll), which gives the user an opaque
    // stack trace they can't act on. See Sentry HRESULT 0x800711C7 reports for context.
    static void EnsureNativeDependenciesLoadable()
    {
        try
        {
            NativeLibrary.Load("SDNative.dll", typeof(Program).Assembly, null);
        }
        catch (Exception ex)
        {
            const uint MB_OK = 0x0;
            const uint MB_ICONERROR = 0x10;
            string message =
                "StarDrive cannot start because the required native library 'SDNative.dll' could not be loaded.\n\n" +
                "This is most often caused by Windows security policies blocking the file:\n" +
                "  • Smart App Control (Windows 11)\n" +
                "  • Windows Defender Application Control (WDAC) or AppLocker\n" +
                "  • Antivirus quarantine\n\n" +
                "How to fix:\n" +
                "  1. Open Windows Security → App & browser control → Smart App Control settings.\n" +
                "     Set it to 'Off' or 'Evaluation'.\n" +
                "  2. Add an exception in your antivirus for the StarDrive install folder.\n" +
                "  3. On a managed (work/school) machine, contact your IT administrator.\n\n" +
                "Need help? Join our Discord server (link is on the game release page) and we'll help you sort it out.\n\n" +
                "Technical details:\n" + ex.Message;

            Win32MessageBox(IntPtr.Zero, message, "StarDrive — Native library blocked", MB_OK | MB_ICONERROR);
            Environment.Exit(NATIVE_DLL_LOAD_FAILURE);
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        AppDomain.CurrentDomain.ProcessExit        += CurrentDomain_ProcessExit;
        Thread.CurrentThread.CurrentCulture   = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture   = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        EnsureNativeDependenciesLoadable();

        try
        {
            // WARNING: This must be called before ANY Log calls
            // @note This will override and initialize global system settings
            GlobalStats.LoadConfig();
            Log.Initialize(enableSentry: true, showHeader: true);
            Thread.CurrentThread.Name = "Main Thread";
            Log.AddThreadMonitor();

            if (!ParseMainArgs(args))
            {
                PrintHelp();
            }
            else
            {
                using StarDriveGame game = new();
                game.OnInitialize = RunInitializationTasks;
                game.Run();
            }

            Log.Write("The game exited normally.");
            RunCleanupAndExit(0);
        }
        catch (EntryPointNotFoundException ex) when (IsMissingDpiApi(ex))
        {
            HandleUnsupportedWindowsVersion(ex);
        }
        catch (Exception ex)
        {
            Log.ErrorDialog(ex, "Game.Run() failed", GAME_RUN_FAILURE);
        }
    }

    // MonoGame 3.8 P/Invokes GetThreadDpiHostingBehavior unconditionally; the API
    // was added in Windows 10 1803 (build 17134). Players on older builds crash
    // inside WinFormsGameForm..ctor with EntryPointNotFoundException naming the
    // missing entry point and USER32.dll.
    static bool IsMissingDpiApi(EntryPointNotFoundException ex)
    {
        return ex.Message.Contains("GetThreadDpi", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("USER32", StringComparison.OrdinalIgnoreCase);
    }

    static void HandleUnsupportedWindowsVersion(EntryPointNotFoundException ex)
    {
        const uint MB_OK = 0x0;
        const uint MB_ICONERROR = 0x10;
        string message =
            "StarDrive cannot start because your version of Windows is missing APIs the\n" +
            "MonoGame 3.8 renderer requires.\n\n" +
            "Minimum supported Windows version:\n" +
            "  • Windows 10 version 1803 (April 2018 Update, build 17134) or later\n" +
            "  • Windows 11 (any build)\n\n" +
            "How to fix:\n" +
            "  1. Open Settings -> Windows Update and install all available updates.\n" +
            "  2. Restart and try launching the game again.\n\n" +
            "Need help? Join our Discord (link is on the game release page).\n\n" +
            "Technical details:\n" + ex.Message;

        // Log once so we keep visibility on how many players this affects;
        // Log.Error has built-in rate limiting so repeat crashes won't flood Sentry.
        Log.Error($"Unsupported Windows version: {ex.Message}");
        Win32MessageBox(IntPtr.Zero, message, "StarDrive — Windows version too old", MB_OK | MB_ICONERROR);
        Environment.Exit(WIN_VERSION_TOO_OLD);
    }
}
