using Microsoft.Extensions.DependencyInjection;
using WolvenKit.Common;
using WolvenKit.Common.Interfaces;
using WolvenKit.Common.Services;
using WolvenKit.Core.Interfaces;
using WolvenKit.Core.Services;
using WolvenKit.Modkit.RED4;
using WolvenKit.RED4.Archive.CR2W;
using WolvenKit.RED4.CR2W;
using WolvenKit.RED4.CR2W.Archive;

namespace REDbridge;

/// <summary>
/// Owns WolvenKit's services and the loaded game. Wired the same way WolvenKit's own CLI
/// wires them (WolvenKit.CLI/GenericHost.cs), minus the pieces the bridge does not use yet.
/// </summary>
public sealed class BridgeHost
{
    public static readonly string Version = typeof(BridgeHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static readonly string WolvenKitVersion = typeof(CR2WFile).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private readonly ServiceProvider _services;

    public BridgeHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerService, StderrLoggerService>();
        services.AddSingleton<IProgressService<double>, PercentProgressService>();
        services.AddSingleton<IHashService, HashService>();
        services.AddSingleton<IHookService, HookService>();
        services.AddSingleton<Red4ParserService>();
        services.AddSingleton<IArchiveManager, ArchiveManager>();
        services.AddSingleton<IModTools, ModTools>();
        _services = services.BuildServiceProvider();
    }

    public ILoggerService Log => _services.GetRequiredService<ILoggerService>();
    public IHashService Hashes => _services.GetRequiredService<IHashService>();
    public Red4ParserService Parser => _services.GetRequiredService<Red4ParserService>();
    public IArchiveManager Archives => _services.GetRequiredService<IArchiveManager>();
    public IModTools ModTools => _services.GetRequiredService<IModTools>();

    /// <summary>The game folder the archives were loaded from, or null before <see cref="LoadGame"/>.</summary>
    public string? GameDir { get; private set; }

    /// <summary>
    /// Loads the base game and Phantom Liberty archives from a Cyberpunk 2077 install.
    /// Only the archive indexes are read, so the game does not need to be running.
    /// </summary>
    public void LoadGame(string gameDir)
    {
        var exe = new FileInfo(Path.Combine(gameDir, "bin", "x64", "Cyberpunk2077.exe"));
        if (!exe.Exists)
        {
            throw new FileNotFoundException($"Cyberpunk2077.exe not found under '{gameDir}'", exe.FullName);
        }

        var started = DateTime.Now;
        Hashes.Loaded.GetAwaiter().GetResult();
        Archives.LoadGameArchives(exe);
        GameDir = gameDir;

        var files = Archives.GetGameArchives().Sum(a => a.Files.Count);
        Log.Info($"Loaded {Archives.GetGameArchives().Count()} archives, {files:N0} files, in {(DateTime.Now - started).TotalSeconds:F1} s");
    }
}
