using System.Text.Json;
using StreamJsonRpc;

namespace REDbridge;

/// <summary>
/// The names of the game's sound events, for a sound browser. The game's sound effects sit in
/// <c>.opuspak</c> files indexed by <c>sfx_container.opusinfo</c>, known there only by number; the
/// index groups them under event hashes, and an event's hash is Wwise's FNV-1 of its lower-case
/// name. WolvenKit ships the event names it knows (from game version 1.6) as
/// <c>Resources/soundEvents.json</c>; this returns them with their hashes so a client can name the
/// groups. Asked for by REDcode's audio player (C5 V2).
/// </summary>
public sealed partial class BridgeApi
{
    public sealed record SoundEvent(string Name, uint Hash, IReadOnlyList<string> Tags);

    public sealed record SoundEvents(string? GameVersion, int Count, IReadOnlyList<SoundEvent> Events);

    private static SoundEvents? _soundEvents;

    [JsonRpcMethod("sound.events")]
    public SoundEvents SoundEventList()
    {
        if (_soundEvents is not null)
        {
            return _soundEvents;
        }
        var file = Path.Combine(AppContext.BaseDirectory, "Resources", "soundEvents.json");
        if (!File.Exists(file))
        {
            throw new FileNotFoundException($"WolvenKit's sound event list is not beside the bridge: {file}");
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        var events = new List<SoundEvent>();
        foreach (var e in root.GetProperty("events").EnumerateArray())
        {
            var name = e.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            var tags = e.TryGetProperty("tags", out var t) ? t.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [];
            events.Add(new SoundEvent(name, WwiseHash(name), tags));
        }
        var version = root.TryGetProperty("gameVersion", out var v) ? v.GetString() : null;
        return _soundEvents = new SoundEvents(version, events.Count, events);
    }

    /// <summary>Wwise's id for a name: 32-bit FNV-1 (multiply, then xor) over the lower-case ASCII bytes.</summary>
    public static uint WwiseHash(string name)
    {
        var h = 2166136261u;
        foreach (var b in System.Text.Encoding.ASCII.GetBytes(name.ToLowerInvariant()))
        {
            h *= 16777619u;
            h ^= b;
        }
        return h;
    }
}
