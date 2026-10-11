using System.Globalization;
using StreamJsonRpc;
using WolvenKit.Common;
using WolvenKit.Common.Conversion;
using WolvenKit.Common.FNV1A;
using WolvenKit.Core.Interfaces;
using WolvenKit.RED4.Archive.CR2W;
using WolvenKit.RED4.Archive.IO;
using WolvenKit.RED4.CR2W;
using WolvenKit.RED4.CR2W.JSON;

namespace REDbridge;

/// <summary>
/// The JSON-RPC surface. Method names are dotted groups, as in the REDcode design.
/// Hashes travel as decimal strings, because JavaScript numbers cannot hold 64 bits.
/// </summary>
public sealed class BridgeApi
{
    private readonly BridgeHost _host;

    public BridgeApi(BridgeHost host) => _host = host;

    // ---- bridge ---------------------------------------------------------------------------

    public sealed record BridgeInfo(string Version, string WolvenKit, string? GameDir, bool GameLoaded, int Archives, long Files);

    [JsonRpcMethod("bridge.info")]
    public BridgeInfo Info()
    {
        var loaded = _host.GameDir is not null;
        var archives = loaded ? _host.Archives.GetGameArchives().ToList() : [];
        return new BridgeInfo(BridgeHost.Version, BridgeHost.WolvenKitVersion, _host.GameDir, loaded,
            archives.Count, archives.Sum(a => (long)a.Files.Count));
    }

    // ---- archive --------------------------------------------------------------------------

    public sealed record ArchiveInfo(string Name, string Source, int Files, string Path);
    public sealed record FileInfoDto(string Hash, string Name, uint Size, string Archive);
    public sealed record FileList(long Total, IReadOnlyList<FileInfoDto> Items);

    [JsonRpcMethod("archive.load")]
    public BridgeInfo Load(string gameDir)
    {
        _host.LoadGame(gameDir);
        return Info();
    }

    [JsonRpcMethod("archive.archives")]
    public IReadOnlyList<ArchiveInfo> Archives()
    {
        RequireGame();
        return _host.Archives.GetGameArchives()
            .Select(a => new ArchiveInfo(a.Name, a.Source.ToString(), a.Files.Count, a.ArchiveAbsolutePath))
            .OrderBy(a => a.Name)
            .ToList();
    }

    /// <summary>Pages through the files of one archive, or of every game archive, optionally filtered by a name prefix.</summary>
    [JsonRpcMethod("archive.list")]
    public FileList List(string? archive = null, string? prefix = null, int offset = 0, int limit = 500)
    {
        RequireGame();
        limit = Math.Clamp(limit, 1, 5000);
        var archives = _host.Archives.GetGameArchives().Where(a => archive is null || a.Name == archive);
        var query = archives.SelectMany(a => a.Files.Values.Select(f => (file: f, archive: a.Name)));
        if (!string.IsNullOrEmpty(prefix))
        {
            query = query.Where(x => x.file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        var all = query.OrderBy(x => x.file.Name, StringComparer.Ordinal).ToList();
        var page = all.Skip(offset).Take(limit).Select(x => ToDto(x.file, x.archive)).ToList();
        return new FileList(all.Count, page);
    }

    [JsonRpcMethod("archive.search")]
    public FileList Search(string query, int limit = 200)
    {
        RequireGame();
        limit = Math.Clamp(limit, 1, 5000);
        var hits = _host.Archives.Search(query, ArchiveManagerScope.Basegame);
        return new FileList(hits.Count, hits.Take(limit).Select(f => ToDto(f, f.GetArchive().Name)).ToList());
    }

    public sealed record ExtractResult(string File, long Bytes);

    [JsonRpcMethod("archive.extract")]
    public ExtractResult Extract(string path, string outFile)
    {
        var file = Find(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        using var fs = File.Create(outFile);
        file.Extract(fs);
        return new ExtractResult(Path.GetFullPath(outFile), fs.Length);
    }

    // ---- cr2w -----------------------------------------------------------------------------

    public sealed record JsonResult(string Path, string Hash, string? File, long Bytes, string? Json);

    /// <summary>
    /// Converts a game file to WolvenKit's CR2W JSON. With <paramref name="outFile"/> the JSON is
    /// written there and only the path comes back; without it the JSON text is returned inline.
    /// </summary>
    [JsonRpcMethod("cr2w.toJson")]
    public JsonResult ToJson(string path, string? outFile = null)
    {
        var file = Find(path);
        var cr2w = ReadCr2w(file);
        var json = RedJsonSerializer.Serialize(new RedFileDto(cr2w));
        if (outFile is null)
        {
            return new JsonResult(file.Name, Hash(file.Key), null, json.Length, json);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        File.WriteAllText(outFile, json);
        return new JsonResult(file.Name, Hash(file.Key), Path.GetFullPath(outFile), new FileInfo(outFile).Length, null);
    }

    /// <summary>Writes a CR2W file from WolvenKit JSON, given inline or as a file path.</summary>
    [JsonRpcMethod("cr2w.fromJson")]
    public ExtractResult FromJson(string outFile, string? json = null, string? jsonFile = null)
    {
        json ??= jsonFile is not null ? File.ReadAllText(jsonFile) : throw new ArgumentException("json or jsonFile is required");
        var dto = RedJsonSerializer.Deserialize<RedFileDto>(json) ?? throw new InvalidDataException("JSON is not a WolvenKit file");
        var cr2w = dto.Data ?? throw new InvalidDataException("JSON has no Data");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        using var fs = File.Create(outFile);
        using var writer = new CR2WWriter(fs) { LoggerService = _host.Log };
        writer.WriteFile(cr2w);
        return new ExtractResult(Path.GetFullPath(outFile), fs.Length);
    }

    // ---- texture --------------------------------------------------------------------------

    [JsonRpcMethod("texture.toPng")]
    public ExtractResult ToPng(string path, string outFile)
    {
        var file = Find(path);
        var cr2w = ReadCr2w(file);
        using var image = RedImage.FromRedFile(cr2w);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        image.SaveToPNG(outFile);
        return new ExtractResult(Path.GetFullPath(outFile), new FileInfo(outFile).Length);
    }

    // ---- hash -----------------------------------------------------------------------------

    public sealed record HashResult(string Value, string Hash, string Hex);

    [JsonRpcMethod("hash.fnv1a64")]
    public HashResult Fnv1a64(string value)
    {
        var h = FNV1A64HashAlgorithm.HashString(value);
        return new HashResult(value, Hash(h), $"0x{h:X16}");
    }

    public sealed record ResolveResult(string Hash, string? Path, bool Known);

    [JsonRpcMethod("hash.resolve")]
    public ResolveResult Resolve(string hash)
    {
        var key = ParseHash(hash);
        var path = _host.Hashes.Get(key);
        return new ResolveResult(Hash(key), path, path is not null);
    }

    // ---- helpers --------------------------------------------------------------------------

    private void RequireGame()
    {
        if (_host.GameDir is null)
        {
            throw new InvalidOperationException("No game loaded. Start with --game or call archive.load first.");
        }
    }

    /// <summary>Accepts a resource path (base\...) or a decimal or 0x hash.</summary>
    private IGameFile Find(string pathOrHash)
    {
        RequireGame();
        var byHash = TryParseHash(pathOrHash, out var key)
            ? _host.Archives.Lookup(key)
            : _host.Archives.Lookup(pathOrHash.Replace('/', '\\'));
        return byHash.HasValue ? byHash.Value : throw new FileNotFoundException($"'{pathOrHash}' is not in the loaded archives");
    }

    private CR2WFile ReadCr2w(IGameFile file)
    {
        using var ms = new MemoryStream();
        file.Extract(ms);
        ms.Position = 0;
        return _host.Parser.ReadRed4File(ms) ?? throw new InvalidDataException($"'{file.Name}' is not a CR2W file");
    }

    private static FileInfoDto ToDto(IGameFile f, string archive) => new(Hash(f.Key), f.Name, f.Size, archive);

    private static string Hash(ulong key) => key.ToString(CultureInfo.InvariantCulture);

    private static ulong ParseHash(string s) =>
        TryParseHash(s, out var v) ? v : throw new ArgumentException($"'{s}' is not a hash");

    private static bool TryParseHash(string s, out ulong value)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }
        return ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
