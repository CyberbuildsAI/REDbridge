using System.Text.Json;
using StreamJsonRpc;
using WolvenKit.Core.CRC;
using WolvenKit.RED4.CR2W.JSON;
using WolvenKit.RED4.TweakDB;
using WolvenKit.RED4.Types;
using WolvenKit.RED4.Types.Pools;

namespace REDbridge;

/// <summary>
/// TweakDB as the game ships it, read offline from <c>r6\cache\tweakdb_ep1.bin</c> (or
/// <c>tweakdb.bin</c> without Phantom Liberty) through WolvenKit's reader, so no game needs to run.
/// Loaded once, on the first call. Ids are hashes (CRC32 of the path, its length in bits 32 to 39);
/// paths come from WolvenKit's names list, which the hash service loads. A record's flats are found
/// from the record id by extending the hash with ".name", so they read even when the record's own
/// path is unknown. Serves REDcode's TweakDB view (C5 V3), REDnode's T3 through its bridge,
/// EDGEmat (REDbridge issue #4) and REDbridge issue #3 (<c>tweakdb.names</c>).
/// </summary>
public sealed partial class BridgeApi
{
    private sealed class TweakIndex
    {
        public required TweakDB Db { get; init; }
        public required string File { get; init; }
        /// <summary>Every record: id, path when known, type. Sorted by path, then the unnamed by id.</summary>
        public required List<(ulong Id, string? Path, Type Type)> Records { get; init; }
        public required Dictionary<ulong, Type> Types { get; init; }
        public required double Seconds { get; init; }
    }

    private static TweakIndex? _tweak;
    private static readonly object TweakLock = new();

    /// <summary>The 40 bits that identify an id: the CRC and the length. The top 24 can carry a flat-buffer offset.</summary>
    private const ulong IdMask = 0xFF_FFFF_FFFF;

    private TweakIndex Tweak()
    {
        lock (TweakLock)
        {
            if (_tweak is not null)
            {
                return _tweak;
            }
            RequireGame();
            var cache = Path.Combine(_host.GameDir!, "r6", "cache");
            var file = new[] { "tweakdb_ep1.bin", "tweakdb.bin" }.Select(f => Path.Combine(cache, f)).FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException($"No tweakdb.bin under {cache}");
            var started = DateTime.Now;
            _host.Hashes.Loaded.GetAwaiter().GetResult();
            using var fs = File.OpenRead(file);
            using var reader = new TweakDBReader(fs);
            var error = reader.ReadFile(out var db);
            if (error != EFileReadErrorCodes.NoError || db is null)
            {
                throw new InvalidDataException($"WolvenKit could not read {file}: {error}");
            }
            var records = new List<(ulong, string?, Type)>();
            var types = new Dictionary<ulong, Type>();
            foreach (var (id, type) in db.Records)
            {
                var key = (ulong)id & IdMask;
                types[key] = type;
                records.Add((key, TweakDBIDPool.ResolveHash(key), type));
            }
            records.Sort((a, b) => a.Item2 is null || b.Item2 is null
                ? (a.Item2 is null).CompareTo(b.Item2 is null) is var n && n != 0 ? n : a.Item1.CompareTo(b.Item1)
                : string.CompareOrdinal(a.Item2, b.Item2));
            var seconds = (DateTime.Now - started).TotalSeconds;
            _host.Log.Info($"TweakDB: {Path.GetFileName(file)}, {records.Count:N0} records, {db.Flats.Count():N0} flats, in {seconds:F1} s");
            return _tweak = new TweakIndex { Db = db, File = file, Records = records, Types = types, Seconds = seconds };
        }
    }

    // ---- results --------------------------------------------------------------------------

    public sealed record TweakType(string Type, int Count);

    public sealed record TweakTypes(string File, int Records, double LoadSeconds, IReadOnlyList<TweakType> Types);

    /// <summary>A record. <see cref="Path"/> is null when the names list does not know the id.</summary>
    public sealed record TweakRecord(string Id, string? Path, string Type, IReadOnlyDictionary<string, object?>? Flats);

    public sealed record TweakRecords(int Total, int Offset, IReadOnlyList<TweakRecord> Items);

    public sealed record TweakFlat(string Name, string Type, object? Value);

    public sealed record TweakRecordFull(string Id, string? Path, string Type, IReadOnlyList<TweakFlat> Flats);

    public sealed record TweakFlatValue(string Id, string? Path, string Type, object? Value);

    /// <summary>A LocKey: <see cref="Key"/> is "LocKey#n"; <see cref="Text"/> is filled when asked for and known.</summary>
    public sealed record LocKeyValue(string Key, string? Text);

    // ---- methods --------------------------------------------------------------------------

    /// <summary>The record types and how many records each has, most first.</summary>
    [JsonRpcMethod("tweakdb.types")]
    public TweakTypes TweakDbTypes()
    {
        var t = Tweak();
        var types = t.Records.GroupBy(r => r.Type).Select(g => new TweakType(TypeName(g.Key), g.Count())).OrderByDescending(x => x.Count).ThenBy(x => x.Type).ToList();
        return new TweakTypes(Path.GetFileName(t.File), t.Records.Count, t.Seconds, types);
    }

    /// <summary>
    /// Records by type (a RED class name such as gamedataClothing_Record, or the short form Clothing;
    /// subtypes included), by path prefix, or both, paged. With <paramref name="flats"/>, each record
    /// carries those flats; "*" gives all of them. Record links come back as paths (an id when the
    /// name is unknown), LocKeys as { key, text } with the text when <paramref name="resolveLocKeys"/>.
    /// </summary>
    [JsonRpcMethod("tweakdb.records")]
    public TweakRecords TweakDbRecords(string? type = null, string? prefix = null, IReadOnlyList<string>? flats = null, int offset = 0, int limit = 200, bool resolveLocKeys = false, string language = "en-us")
    {
        var t = Tweak();
        var wanted = type is null ? null : ResolveRecordType(t, type);
        var hits = t.Records.Where(r =>
            (wanted is null || r.Type.IsAssignableTo(wanted)) &&
            (prefix is null || (r.Path is not null && r.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))).ToList();
        var loc = resolveLocKeys ? LocTexts(language) : null;
        var all = flats is { Count: 1 } && flats[0] == "*";
        var items = hits.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 20000)).Select(r =>
        {
            Dictionary<string, object?>? values = null;
            if (flats is { Count: > 0 })
            {
                values = new Dictionary<string, object?>();
                var source = all ? FlatsOf(t, r.Id, r.Path, r.Type).Select(x => (x.Name, (IRedType?)x.Value)) : flats.Select(name => (name, FlatOf(t, r.Id, name)));
                foreach (var (name, value) in source)
                {
                    values[name] = value is null ? null : ValueOf(value, loc);
                }
            }
            return new TweakRecord(IdText(r.Id), r.Path, TypeName(r.Type), values);
        }).ToList();
        return new TweakRecords(hits.Count, offset, items);
    }

    /// <summary>One record, by path or id, with every flat: its name, its RED type and its value.</summary>
    [JsonRpcMethod("tweakdb.record")]
    public TweakRecordFull TweakDbRecord(string path, bool resolveLocKeys = false, string language = "en-us")
    {
        var t = Tweak();
        var id = IdOf(path);
        if (!t.Types.TryGetValue(id, out var type))
        {
            throw new KeyNotFoundException($"No TweakDB record '{path}'");
        }
        var name = TweakDBIDPool.ResolveHash(id);
        var loc = resolveLocKeys ? LocTexts(language) : null;
        var flats = FlatsOf(t, id, name, type).Select(f => new TweakFlat(f.Name, RedReflection.GetRedTypeFromCSType(f.Value.GetType()), ValueOf(f.Value, loc))).ToList();
        return new TweakRecordFull(IdText(id), name, TypeName(type), flats);
    }

    /// <summary>One flat by its full path (Record.flat) or id.</summary>
    [JsonRpcMethod("tweakdb.flat")]
    public TweakFlatValue TweakDbFlat(string path, bool resolveLocKeys = false, string language = "en-us")
    {
        var t = Tweak();
        var id = IdOf(path);
        var value = t.Db.Flats.GetValue(id) ?? throw new KeyNotFoundException($"No TweakDB flat '{path}'");
        return new TweakFlatValue(IdText(id), TweakDBIDPool.ResolveHash(id), RedReflection.GetRedTypeFromCSType(value.GetType()), ValueOf(value, resolveLocKeys ? LocTexts(language) : null));
    }

    /// <summary>Records whose path contains every word of the text, any case.</summary>
    [JsonRpcMethod("tweakdb.search")]
    public TweakRecords TweakDbSearch(string text, string? type = null, int limit = 200)
    {
        var t = Tweak();
        var wanted = type is null ? null : ResolveRecordType(t, type);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = t.Records.Where(r => r.Path is not null && (wanted is null || r.Type.IsAssignableTo(wanted)) && words.All(w => r.Path.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        return new TweakRecords(hits.Count, 0, hits.Take(Math.Clamp(limit, 1, 20000)).Select(r => new TweakRecord(IdText(r.Id), r.Path, TypeName(r.Type), null)).ToList());
    }

    /// <summary>
    /// Paths for TweakDB ids ("0x..." hex or decimal), from WolvenKit's names list; null when unknown.
    /// Only the low 40 bits count, so an id with a flat-buffer offset in its top bits still matches.
    /// Needs no TweakDB load. Asked for by REDnode (REDbridge issue #3).
    /// </summary>
    [JsonRpcMethod("tweakdb.names")]
    public IReadOnlyDictionary<string, string?> TweakDbNames(IReadOnlyList<string> ids)
    {
        _host.Hashes.Loaded.GetAwaiter().GetResult();
        var result = new Dictionary<string, string?>();
        foreach (var raw in ids)
        {
            result[raw] = TryParseTweakId(raw, out var id) ? TweakDBIDPool.ResolveHash(id & IdMask) : null;
        }
        return result;
    }

    // ---- helpers --------------------------------------------------------------------------

    private static string IdText(ulong id) => $"0x{id & IdMask:X16}";

    private static string TypeName(Type type) => RedReflection.GetTypeRedName(type) ?? type.Name;

    private static bool TryParseTweakId(string raw, out ulong id)
    {
        var s = raw.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out id);
        }
        return ulong.TryParse(s, out id);
    }

    /// <summary>A path, a 0x hex id or a decimal id, as the 40-bit id.</summary>
    private static ulong IdOf(string pathOrId) =>
        TryParseTweakId(pathOrId, out var id) ? id & IdMask : TweakDBID.CalculateHash(pathOrId);

    private static Type ResolveRecordType(TweakIndex t, string type)
    {
        var names = new[] { type, $"gamedata{type}_Record", $"gamedata{type}" };
        foreach (var known in t.Records.Select(r => r.Type).Distinct())
        {
            var name = TypeName(known);
            if (names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            {
                return known;
            }
        }
        // A base type with no records of its own, such as gamedataItem_Record.
        foreach (var n in names)
        {
            try
            {
                var (cs, _) = RedReflection.GetCSTypeFromRedType(n);
                if (cs.IsAssignableTo(typeof(gamedataTweakDBRecord)))
                {
                    return cs;
                }
            }
            catch
            {
                // not a type name
            }
        }
        throw new ArgumentException($"No TweakDB record type '{type}'. tweakdb.types lists them.");
    }

    /// <summary>The flat name of a record, found by extending the record's hash, so it works without the record's path.</summary>
    private static IRedType? FlatOf(TweakIndex t, ulong recordId, string name)
    {
        var crc = (uint)(recordId & 0xFFFF_FFFF);
        var length = (uint)((recordId >> 32) & 0xFF);
        var add = System.Text.Encoding.ASCII.GetBytes("." + name);
        var id = Crc32Algorithm.Append(crc, add) + ((ulong)(length + add.Length) << 32);
        return t.Db.Flats.GetValue(id);
    }

    /// <summary>Every flat of a record: the record type's properties, then flats the names list knows beyond them.</summary>
    private static IEnumerable<(string Name, IRedType Value)> FlatsOf(TweakIndex t, ulong recordId, string? path, Type type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in RedReflection.GetTypeInfo(type).PropertyInfos)
        {
            if (p.RedName is null || !seen.Add(p.RedName))
            {
                continue;
            }
            if (FlatOf(t, recordId, p.RedName) is { } v)
            {
                yield return (p.RedName, v);
            }
        }
        if (path is not null)
        {
            foreach (var (name, value) in t.Db.Flats.GetRecordValues(path))
            {
                if (seen.Add(name))
                {
                    yield return (name, value);
                }
            }
        }
    }

    /// <summary>A flat's value as plain JSON: links as paths, LocKeys as { key, text }, references as paths.</summary>
    private object? ValueOf(IRedType value, IReadOnlyDictionary<ulong, string>? loc) => value switch
    {
        TweakDBID id => TweakDBIDPool.ResolveHash((ulong)id & IdMask) ?? (((ulong)id & IdMask) == 0 ? null : IdText(id)),
        gamedataLocKeyWrapper k => new LocKeyValue($"LocKey#{k.Key}", loc is not null && loc.TryGetValue(k.Key, out var text) ? text : null),
        CName n => n.GetResolvedText(),
        CString s => (string)s,
        CBool b => (bool)b,
        CFloat f => (float)f,
        CDouble d => (double)d,
        CInt8 i => (sbyte)i,
        CInt16 i => (short)i,
        CInt32 i => (int)i,
        CInt64 i => (long)i,
        CUInt8 i => (byte)i,
        CUInt16 i => (ushort)i,
        CUInt32 i => (uint)i,
        CUInt64 i => ((ulong)i).ToString(),
        IRedEnum e => e.ToEnumString(),
        IRedRef r => r.DepotPath.GetResolvedText() ?? (((ulong)r.DepotPath) == 0 ? null : ((ulong)r.DepotPath).ToString()),
        IRedArray a => a.Cast<IRedType>().Select(x => ValueOf(x, loc)).ToList(),
        _ => JsonDocument.Parse(RedJsonSerializer.Serialize(value)).RootElement.Clone(),
    };

    private static readonly Dictionary<string, Dictionary<ulong, string>> _locTexts = new();

    /// <summary>LocKey number to text in a language (the female variant when there is one), from the loc index. Built once per language.</summary>
    private IReadOnlyDictionary<ulong, string> LocTexts(string language)
    {
        var index = GameIndex(language);
        lock (_locTexts)
        {
            if (_locTexts.TryGetValue(language, out var known))
            {
                return known;
            }
            return _locTexts[language] = index.ByKey.ToDictionary(p => p.Key, p => string.IsNullOrEmpty(p.Value.Female) ? p.Value.Male : p.Value.Female);
        }
    }
}
