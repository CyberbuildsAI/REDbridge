using StreamJsonRpc;
using WolvenKit.Common;
using WolvenKit.Common.Model;
using WolvenKit.Common.Model.Arguments;
using WolvenKit.RED4.Archive.IO;
using WolvenKit.RED4.Archive.CR2W;
using WolvenKit.RED4.CR2W;
using WolvenKit.RED4.Types;

namespace REDbridge;

/// <summary>
/// Export and import over WolvenKit's Modkit, for REDcode's C4 S3 and S4. Both work on a project's
/// layout: a file under source/archive exports to the same relative path under source/raw, and a
/// raw file imports back to source/archive. The results list what was written, because one export
/// can write several files (a mesh with its material textures, for instance).
/// </summary>
public sealed partial class BridgeApi
{
    /// <summary>The few export settings worth exposing; everything else keeps WolvenKit's defaults.</summary>
    public sealed record ExportOptions(
        string? TextureFormat = null,
        bool? MeshBinary = null,
        bool? MeshMaterials = null,
        string? MaterialTextureFormat = null,
        bool? MeshLodFilter = null);

    public sealed record ModkitResult(bool Ok, List<string> Written);

    /// <summary>
    /// Exports a file under archiveDir (a project's source/archive) into rawDir (its source/raw), at
    /// the same relative path, the way WolvenKit's Export does: glTF for meshes, an image for
    /// textures, WAV for sounds, and so on. Mesh materials go into rawDir as well, as WolvenKit's
    /// command line puts them in its output folder. Written lists the files created or changed, relative to rawDir.
    /// </summary>
    [JsonRpcMethod("project.export")]
    public ModkitResult Export(string file, string archiveDir, string rawDir, ExportOptions? options = null)
    {
        var source = new FileInfo(file);
        if (!source.Exists)
        {
            throw new FileNotFoundException($"File not found: {file}", file);
        }
        var archive = new DirectoryInfo(archiveDir);
        if (!source.FullName.StartsWith(archive.FullName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{file} is not under {archiveDir}");
        }
        // WolvenKit dumps the raw buffers of any other type, which nobody wants in source/raw.
        var ext = source.Extension.TrimStart('.');
        if (!Enum.TryParse<ECookedFileFormat>(ext, true, out _))
        {
            throw new ArgumentException($".{ext} files have no export format; open them as JSON instead. Exportable: {string.Join(", ", Enum.GetNames<ECookedFileFormat>().Select(n => "." + n))}");
        }
        Directory.CreateDirectory(rawDir);
        // WolvenKit remembers what it exported in this process and skips it the next time.
        _host.ModTools.ClearFileLookup();
        var args = new GlobalExportArgs();
        var o = options ?? new ExportOptions();
        if (o.TextureFormat is { } tf)
        {
            args.Get<XbmExportArgs>().UncookExtension = ParseExtension(tf);
        }
        var mesh = args.Get<MeshExportArgs>();
        if (o.MeshBinary is { } bin)
        {
            mesh.isGLBinary = bin;
        }
        if (o.MeshMaterials is { } mats)
        {
            mesh.withMaterials = mats;
        }
        if (o.MaterialTextureFormat is { } mtf)
        {
            mesh.MaterialUncookExtension = ParseExtension(mtf);
        }
        if (o.MeshLodFilter is { } lod)
        {
            mesh.LodFilter = lod;
        }
        mesh.MaterialRepo = Path.GetFullPath(rawDir);
        args.Get<GeneralExportArgs>().MaterialRepositoryPath = Path.GetFullPath(rawDir);

        var before = Snapshot(rawDir);
        var ok = _host.ModTools.Export(source, args, archive, new DirectoryInfo(rawDir));
        return new ModkitResult(ok, Changed(rawDir, before));
    }

    /// <summary>
    /// Imports a raw file (under rawDir, a project's source/raw) back into archiveDir (its
    /// source/archive), the way WolvenKit's Import does. With keep (the default, as in WolvenKit),
    /// an existing game file at the target keeps its settings: a texture its group and compression,
    /// a mesh its materials. Written lists the files created or changed, relative to archiveDir.
    /// </summary>
    [JsonRpcMethod("project.import")]
    public async Task<ModkitResult> Import(string file, string rawDir, string archiveDir, bool keep = true)
    {
        var source = new FileInfo(file);
        if (!source.Exists)
        {
            throw new FileNotFoundException($"File not found: {file}", file);
        }
        var raw = new DirectoryInfo(rawDir);
        if (!source.FullName.StartsWith(raw.FullName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{file} is not under {rawDir}");
        }
        var ext = source.Extension.TrimStart('.');
        if (!Enum.TryParse<ERawFileFormat>(ext, true, out _))
        {
            throw new ArgumentException($".{ext} files cannot be imported. Importable: {string.Join(", ", Enum.GetNames<ERawFileFormat>().Select(n => "." + n))}");
        }
        Directory.CreateDirectory(archiveDir);
        var args = new GlobalImportArgs();
        args.Get<CommonImportArgs>().Keep = keep;
        args.Get<XbmImportArgs>().Keep = keep;
        args.Get<GltfImportArgs>().Keep = keep;

        var relative = Path.GetRelativePath(raw.FullName, source.FullName);
        // WolvenKit reads the existing texture's setup itself only when it runs as its command line;
        // its app fills the import dialog from that file first. Do what the dialog does.
        var existing = Path.Combine(archiveDir, Path.ChangeExtension(relative, ".xbm"));
        STextureGroupSetup? kept = null;
        if (keep && IsImage(ext) && File.Exists(existing))
        {
            using var fs = File.OpenRead(existing);
            if (_host.Parser.ReadRed4File(fs)?.RootChunk is CBitmapTexture { Setup: { } setup })
            {
                kept = setup;
                args.Register(new XbmImportArgs(setup) { Keep = keep });
            }
        }

        var before = Snapshot(archiveDir);
        var ok = await _host.ModTools.Import(new RedRelativePath(raw, relative), args, new DirectoryInfo(archiveDir));
        if (ok && kept is not null)
        {
            RestoreTextureFlags(existing, kept);
        }
        return new ModkitResult(ok, Changed(archiveDir, before));
    }

    /// <summary>
    /// The setup fields WolvenKit's import settings have no place for, put back from the texture the
    /// import replaced. The rest (group, format, compression, gamma, mips) went in through XbmImportArgs.
    /// </summary>
    private void RestoreTextureFlags(string xbm, STextureGroupSetup was)
    {
        CR2WFile? cr2w;
        using (var fs = File.OpenRead(xbm))
        {
            cr2w = _host.Parser.ReadRed4File(fs);
        }
        if (cr2w?.RootChunk is not CBitmapTexture { Setup: { } now })
        {
            return;
        }
        now.IsStreamable = was.IsStreamable;
        now.AllowTextureDowngrade = was.AllowTextureDowngrade;
        now.PlatformMipBiasPC = was.PlatformMipBiasPC;
        now.PlatformMipBiasConsole = was.PlatformMipBiasConsole;
        using var outFs = File.Create(xbm);
        using var writer = new CR2WWriter(outFs) { LoggerService = _host.Log };
        writer.WriteFile(cr2w);
    }

    private static bool IsImage(string ext) => ext.ToLowerInvariant() is "png" or "tga" or "dds" or "bmp" or "jpg" or "tiff";

    private static EUncookExtension ParseExtension(string value) =>
        Enum.TryParse<EUncookExtension>(value.TrimStart('.'), true, out var e)
            ? e
            : throw new ArgumentException($"Unknown texture format {value}; use one of {string.Join(", ", Enum.GetNames<EUncookExtension>())}");

    /// <summary>Every file under dir with its write time and size, to tell afterwards what an export or import wrote.</summary>
    private static Dictionary<string, (DateTime, long)> Snapshot(string dir)
    {
        var map = new Dictionary<string, (DateTime, long)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir))
        {
            return map;
        }
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            map[f.FullName] = (f.LastWriteTimeUtc, f.Length);
        }
        return map;
    }

    private static List<string> Changed(string dir, Dictionary<string, (DateTime, long)> before)
    {
        var root = Path.GetFullPath(dir);
        return Snapshot(dir)
            .Where(kv => !before.TryGetValue(kv.Key, out var was) || was != kv.Value)
            .Select(kv => Path.GetRelativePath(root, kv.Key))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
