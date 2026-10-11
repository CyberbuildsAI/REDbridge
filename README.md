<p align="center">
  <img src="docs/assets/brand/banner.png" alt="REDbridge: hosts WolvenKit's engine behind JSON-RPC for REDcode and AI assistants. Engine WolvenKit C#, protocol JSON-RPC over stdio, clients REDcode and MCP." width="100%">
</p>

<p align="center">
  <a href="https://cyberbuildsai.github.io/REDbridge/"><img src="docs/assets/brand/badges/docs.svg" alt="docs: read the docs"></a>
  <img src="docs/assets/brand/badges/status.svg" alt="status: design phase">
  <img src="docs/assets/brand/badges/wolvenkit.svg" alt="WolvenKit: 9.1">
  <img src="docs/assets/brand/badges/dotnet.svg" alt=".NET: 10">
  <a href="LICENSE"><img src="docs/assets/brand/badges/license.svg" alt="license: GPL-3.0"></a>
</p>

The engine host for [REDcode](https://github.com/CyberbuildsAI/REDcode), a Cyberpunk 2077 modding IDE built on VS Code.

REDbridge runs [WolvenKit](https://github.com/WolvenKit/WolvenKit)'s headless C# libraries in a local process and exposes them over JSON-RPC: archive browsing, CR2W files as JSON and back, texture, mesh and audio conversion, TweakDB queries, import, export and packing. REDcode's extensions are its first client. An MCP server over the same methods lets AI assistants work a mod project through it too.

**[Read the docs](https://cyberbuildsai.github.io/REDbridge/)** &middot; [REDcode](https://github.com/CyberbuildsAI/REDcode) &middot; [REDnode](https://github.com/CyberbuildsAI/REDnode)

> [!NOTE]
> **Status: C1 bridge MVP works.** The bridge indexes a game install, lists and searches archives, converts CR2W files to JSON and back byte for byte, decodes textures to PNG and resolves hashes, all over JSON-RPC on stdio. `tools/probe.py` exercises it against a real install in about seven seconds. Next: the REDcode extension that talks to it.

## How it works

```
 VS Code (REDcode extensions)      REDbridge (.NET 10)                 WolvenKit libraries
 ┌───────────────────────────┐     ┌────────────────────────────┐     ┌────────────────────┐
 │ archive browser           │     │ JSON-RPC 2.0 over stdio    │     │ RED4: CR2W, archives│
 │ CR2W editor               │◄───►│ method groups: archive,    │◄───►│ Common: JSON, DDS   │
 │ import / export / pack    │     │ cr2w, convert, modkit,     │     │ Modkit: glTF, pack  │
 │ AI assistants via MCP     │     │ tweakdb, hash, script, mcp │     │ Core: Oodle, hashes │
 └───────────────────────────┘     └────────────────────────────┘     └────────────────────┘
                                    big outputs (PNG, glTF, WAV) go to a cache folder; RPC returns paths
```

| Method group | Examples | Backed by |
|---|---|---|
| `archive` | list, search, extract, conflicts | WolvenKit.RED4 |
| `cr2w` | toJson, fromJson, getChunk, patch, createInstance | WolvenKit.RED4, WolvenKit.Common |
| `convert` | texture.toPng, mesh.toGltf, audio.toWav | WolvenKit.Common, WolvenKit.Modkit |
| `modkit` | import, export, pack, build | WolvenKit.Modkit |
| `tweakdb` | records, record, flats, query | WolvenKit.RED4 |
| `hash` | fnv1a64, resolve | WolvenKit.Core |
| `script` | run a `wkit`-compatible script | ClearScript V8 |
| `mcp` | the same methods for AI assistants | REDbridge |

## Repository layout

| Path | Contents |
|---|---|
| `docs/` | Documentation site |
| `tools/brand/` | Generates the banner, badges and icon |
| `src/REDbridge/` | The bridge: host, RPC surface, stderr logger |
| `tools/probe.py` | Stdlib Python client that runs every method over the real protocol |
| `external/WolvenKit/` | WolvenKit as a git submodule, pinned to 9.1.0 |

## Build

```powershell
git clone --recurse-submodules https://github.com/CyberbuildsAI/REDbridge.git
cd REDbridge
dotnet restore src\REDbridge\REDbridge.csproj
dotnet build src\REDbridge\REDbridge.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false -v q
python -I tools\probe.py (Resolve-Path src\REDbridge\bin\Release\net10.0\REDbridge.exe) "<Cyberpunk 2077 folder>" probe-out
```

Needs the .NET 10 SDK and Python 3. The build compiles WolvenKit from the submodule, so the first run takes about a minute. The probe loads the game's archive indexes and exercises every method; the game does not need to be running. Details, including the per-user SDK route without administrator rights, are in the [setup guide](https://cyberbuildsai.github.io/REDbridge/dev-setup.html).

## Roadmap

- **C0 Decide.** Build WolvenKit's headless libraries locally and confirm what the bridge references. Agree the approach with the WolvenKit team.
- **C1 Bridge MVP.** `archive.list`, `cr2w.toJson`, `cr2w.fromJson`, `texture.toPng`, `hash.*` over stdio. Proves the data path with REDcode.
- **C2 to C4.** Method groups for browsing, editing, import, export and packing, as REDcode's extensions need them.
- **C5.** Script host and MCP server.

Phases follow the REDcode roadmap.

## Compatibility goals

REDbridge pins a WolvenKit version and tracks its releases. Changes that make WolvenKit's libraries easier to host are sent upstream. Windows only for now, because WolvenKit's texture tooling is.

## Legal

REDbridge links WolvenKit's libraries, which are GPL-3.0, so REDbridge is GPL-3.0. REDcode's extensions talk to it only over stdio and keep their own license. This repository contains no game files and no data extracted from them. Single-player modding only.

## License

[GPL-3.0](LICENSE)
