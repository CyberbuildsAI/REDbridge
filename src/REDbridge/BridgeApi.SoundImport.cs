using System.Diagnostics;
using StreamJsonRpc;
using WolvenKit.Modkit.RED4.Opus;

namespace REDbridge;

/// <summary>
/// Overrides game sound effects in a project from WAV files (REDbridge issue #6, for EDGEwave's A3 and
/// REDcode's Import). The same steps as WolvenKit's <c>OpusTools.ImportWavs</c>, which the app's sound
/// import uses: each WAV is encoded with opus-tools' <c>opusenc</c>, decoded once with <c>opusdec</c> so
/// the PCM length written to the index is the one the game decodes, and encoded again; then
/// WolvenKit's <c>OpusInfo</c> writes it into a copy of its <c>.opuspak</c> and a new
/// <c>sfx_container.opusinfo</c>. Unlike <c>ImportWavs</c>, it works on copies of the WAVs in a temporary
/// folder (ImportWavs writes the decoded WAV over its input), builds on the project's own index and
/// packs under archiveDir when they are there, so earlier overrides stay, and creates the folders.
/// WolvenKit.Modkit's own import of a WAV is a stub, so <c>project.import</c> cannot do this.
/// </summary>
public sealed partial class BridgeApi
{
    public sealed record SkippedWav(string File, string Reason);

    /// <summary><see cref="Written"/> is relative to archiveDir; <see cref="Imported"/> lists the sound hashes replaced.</summary>
    public sealed record SoundImportResult(bool Ok, IReadOnlyList<uint> Imported, IReadOnlyList<string> Written, IReadOnlyList<SkippedWav> Skipped);

    private const string SoundBanks = @"base\sound\soundbanks";

    /// <summary>
    /// Replaces the sounds named by <paramref name="wavFiles"/> (each <c>&lt;sound hash&gt;.wav</c>, the decimal
    /// hash in sfx_container.opusinfo) in the project whose source/archive is <paramref name="archiveDir"/>.
    /// opusenc takes 16-bit, 24-bit or float WAV at any rate and encodes at 48 kHz, as the game's packs are.
    /// </summary>
    [JsonRpcMethod("sound.importWavs")]
    public SoundImportResult ImportWavs(IReadOnlyList<string> wavFiles, string archiveDir)
    {
        RequireGame();
        var tools = Path.Combine(AppContext.BaseDirectory, "opus-tools");
        if (!File.Exists(Path.Combine(tools, "opusenc.exe")) || !File.Exists(Path.Combine(tools, "opusdec.exe")))
        {
            throw new FileNotFoundException($"opus-tools are not beside the bridge: {tools}");
        }
        var outDir = Path.Combine(Path.GetFullPath(archiveDir), SoundBanks);
        var info = new OpusInfo(OpenSoundBank(outDir, "sfx_container.opusinfo"));
        var indexOf = new Dictionary<uint, int>();
        for (var i = 0; i < info.OpusCount; i++)
        {
            indexOf.TryAdd(info.OpusHashes[i], i);
        }

        var skipped = new List<SkippedWav>();
        var imported = new List<uint>();
        var packs = new Dictionary<int, Stream>();
        var work = Directory.CreateTempSubdirectory("redbridge-wav-");
        try
        {
            foreach (var wav in wavFiles)
            {
                if (!uint.TryParse(Path.GetFileNameWithoutExtension(wav), out var hash))
                {
                    skipped.Add(new SkippedWav(wav, "The file name is not a sound hash: name it <hash>.wav, such as 832549876.wav"));
                    continue;
                }
                if (!indexOf.TryGetValue(hash, out var at))
                {
                    skipped.Add(new SkippedWav(wav, $"Sound {hash} is not in the game's sound effect index"));
                    continue;
                }
                if (!File.Exists(wav))
                {
                    skipped.Add(new SkippedWav(wav, "The file does not exist"));
                    continue;
                }
                var copy = Path.Combine(work.FullName, $"{hash}.wav");
                var opus = Path.ChangeExtension(copy, ".opus");
                File.Copy(wav, copy, true);
                // Encode, decode to the PCM the game will decode (its length goes into the index), encode again.
                var failure = OpusTool(tools, "opusenc.exe", $"\"{copy}\" \"{opus}\" --serial 42 --quiet --padding 0 --vbr --comp 10 --framesize 20")
                    ?? OpusTool(tools, "opusdec.exe", $"--quiet \"{opus}\" \"{copy}\"")
                    ?? OpusTool(tools, "opusenc.exe", $"\"{copy}\" \"{opus}\" --serial 42 --quiet --padding 0 --vbr --comp 10 --framesize 20");
                if (failure is not null || !File.Exists(opus))
                {
                    skipped.Add(new SkippedWav(wav, failure ?? "opusenc wrote no file"));
                    continue;
                }
                int pack = info.PackIndices[at];
                if (!packs.TryGetValue(pack, out var stream))
                {
                    stream = OpenSoundBank(outDir, $"sfx_container_{pack}.opuspak");
                }
                packs[pack] = info.WriteOpusToPak(new MemoryStream(File.ReadAllBytes(opus)), stream, hash, new MemoryStream(File.ReadAllBytes(copy)));
                imported.Add(hash);
            }
        }
        finally
        {
            try
            {
                work.Delete(true);
            }
            catch
            {
                // a temporary folder left behind is harmless
            }
        }

        var written = new List<string>();
        if (imported.Count > 0)
        {
            Directory.CreateDirectory(outDir);
            foreach (var (pack, stream) in packs)
            {
                var name = $"sfx_container_{pack}.opuspak";
                stream.Position = 0;
                using (var fs = File.Create(Path.Combine(outDir, name)))
                {
                    stream.CopyTo(fs);
                }
                written.Add(Path.Combine(SoundBanks, name));
            }
            info.WriteOpusInfo(new DirectoryInfo(outDir));
            written.Add(Path.Combine(SoundBanks, "sfx_container.opusinfo"));
            _host.Log.Info($"sound.importWavs: {imported.Count} sound(s) into {written.Count - 1} pack(s) under {archiveDir}");
        }
        return new SoundImportResult(imported.Count > 0, imported, written, skipped);
    }

    /// <summary>A file of the sound banks: the project's copy under outDir when there is one, so earlier overrides stay, else the game's.</summary>
    private MemoryStream OpenSoundBank(string outDir, string name)
    {
        var ms = new MemoryStream();
        var local = Path.Combine(outDir, name);
        if (File.Exists(local))
        {
            using var fs = File.OpenRead(local);
            fs.CopyTo(ms);
        }
        else
        {
            Find(Path.Combine(SoundBanks, name)).Extract(ms);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>Runs one of opus-tools; null when it succeeded, else what went wrong.</summary>
    private static string? OpusTool(string tools, string exe, string arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(tools, exe), arguments)
        {
            WorkingDirectory = tools,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {exe}");
        var stderr = p.StandardError.ReadToEndAsync();
        p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(120_000))
        {
            p.Kill(true);
            return $"{exe} did not finish within two minutes";
        }
        return p.ExitCode == 0 ? null : $"{exe} failed ({p.ExitCode}): {stderr.Result.Trim()}";
    }
}
