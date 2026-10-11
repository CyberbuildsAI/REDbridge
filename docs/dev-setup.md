# Development setup

Everything needed to work on REDbridge, in order. A fresh machine should be able to follow this top to bottom. The shared tools (Git, Python, winget) are the same as in [REDnode's setup guide](https://cyberbuildsai.github.io/REDnode/dev-setup.html).

Reference machine: Windows 11 Home (build 26200), 16 GB RAM, no administrator session available.

## 1. Base tools

| Tool | Version used | Why | Install |
|---|---|---|---|
| Git for Windows | 2.55.0 | Source control | `winget install --id Git.Git -e` |
| Python | 3.14.6 | Brand tooling, small scripts | Any Python 3.10 or later |
| Chrome or Edge | any | Renders the brand images | Already present on Windows |

Git needs long paths on Windows, because WolvenKit's generated type files exceed the default limit. Without it a clone stops with "Filename too long" and leaves the checkout incomplete.

```powershell
git config --global core.longpaths true
```

## 2. .NET SDK

WolvenKit targets .NET 10 (`global.json` asks for SDK 10.0.302 or a later feature band).

| Tool | Version used | Install |
|---|---|---|
| .NET SDK | 10.0.401 | Per-user, no administrator rights, see below |

The per-user install uses Microsoft's `dotnet-install.ps1`. It needs no UAC prompt, so it works over a remote session. It puts the SDK in `%LOCALAPPDATA%\Microsoft\dotnet`, about 770 MB.

```powershell
$dl = "$env:TEMP\dotnet-install"; New-Item -ItemType Directory -Force $dl | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile "$dl\dotnet-install.ps1"
$root = "$env:LOCALAPPDATA\Microsoft\dotnet"
& "$dl\dotnet-install.ps1" -Channel 10.0 -InstallDir $root -NoPath
# make it the default for this user; new shells only
[Environment]::SetEnvironmentVariable('Path', "$root;" + [Environment]::GetEnvironmentVariable('Path','User'), 'User')
[Environment]::SetEnvironmentVariable('DOTNET_ROOT', $root, 'User')
& "$root\dotnet.exe" --list-sdks
```

A machine-wide `dotnet.exe` in `C:\Program Files\dotnet` may exist as a runtime only. It does not see the per-user SDK, so the per-user folder must come first on `PATH`. With administrator rights, `winget install --id Microsoft.DotNet.SDK.10 -e` is the simpler route.

## 3. WolvenKit source

REDbridge builds against WolvenKit's headless libraries at a pinned commit, wired as a git submodule with project references. WolvenKit's binaries are never committed or redistributed by this repo; a build produces them.

| Pinned commit | WolvenKit version |
|---|---|
| `4d1716b1` | 9.1.0 (2026-10-10) |

Headless projects: `WolvenKit.Core`, `WolvenKit.Common`, `WolvenKit.RED4`, `WolvenKit.Modkit`. The WPF projects (`WolvenKit`, `WolvenKit.App`) are not needed and not built.

### Building the libraries

The submodule is not built on its own. Building the bridge (section 5) builds `Core`, `RED4`, `Common` and `Modkit` from `external/WolvenKit` as project references. After a plain `git clone`, fetch the submodule first:

```powershell
git submodule update --init
```

Build one project at a time with parallelism off and without the shared compiler server: the parallel build was killed for lack of memory on the 16 GB laptop, and the single-threaded build is fast anyway. These flags are already in the bridge build command in section 5.

| Project | Build time (single-threaded) | Output |
|---|---|---|
| WolvenKit.Core | seconds | 0.1 MB |
| WolvenKit.RED4 | 39 s | 11.6 MB |
| WolvenKit.Common | 7 s | 16.4 MB |
| WolvenKit.Modkit | 8 s | 0.5 MB |

### What the Modkit output contains

`WolvenKit.Modkit/bin/Release/net10.0`: 129 MB, 25 files. This is the set a bridge that references all four libraries has to ship, before trimming.

| Group | Files | Notes |
|---|---|---|
| Managed WolvenKit assemblies | `WolvenKit.{Core,RED4,Common,Modkit}.dll` | 29 MB together, GPL-3.0 |
| Oodle decompression | `kraken.dll`, `libkraken.so`, `libkraken.dylib` | Linux and macOS builds exist. Origin and license of these binaries: not yet verified |
| Textures | `DirectXTexNet.dll`, `lib/texconv.dll`, `lib/DirectXTexNetImpl.dll`, `lib/Ijwhost.dll` | Windows only. This is what keeps the bridge Windows-only |
| Wwise audio | `wwtools.dll`, `libwwtools.so`, `lib/COPYING` | `COPYING` is vgmstream's license |
| Audio encoding | `libmp3lame.32.dll`, `libmp3lame.64.dll`, `opus-tools/opus{dec,enc,info}.exe` | LAME is LGPL and comes from NAudio.Lame. The bridge does not need MP3 encoding; drop it when trimming |
| Data | `Resources/soundEvents.json` | |
| NuGet packages | 49, listed in `WolvenKit.Modkit.deps.json` | Includes ClearScript V8 (scripting), EF Core Sqlite, SharpGLTF, SharpDX, NAudio, YamlDotNet, Newtonsoft.Json |

The ClearScript V8 native library (`ClearScriptV8.win-x64.dll`) is not in this build output. It is expected to appear on a runtime-specific publish (`dotnet publish -r win-x64`). Unverified until C5, when the script host is built.

## 4. Repository and GitHub

```powershell
git clone --recurse-submodules https://github.com/CyberbuildsAI/REDbridge.git
cd REDbridge
git config user.name  "CyberbuildsAI"
git config user.email "340969962+CyberbuildsAI@users.noreply.github.com"
```

The submodule is added in C1. Until then there is nothing to build in this repo.

## 5. Building and running the bridge

From clone to a working bridge, in order. Verified on both reference machines on 2026-10-11.

```powershell
git clone --recurse-submodules https://github.com/CyberbuildsAI/REDbridge.git
cd REDbridge
git submodule update --init      # only needed if external\WolvenKit is empty (plain clone, or after a pull)
dotnet restore src\REDbridge\REDbridge.csproj
dotnet build src\REDbridge\REDbridge.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false -v q
python -I tools\probe.py (Resolve-Path src\REDbridge\bin\Release\net10.0\REDbridge.exe) "<Cyberpunk 2077 folder>" probe-out
```

What each step does:

| Step | Notes |
|---|---|
| `git submodule update --init` | `git pull` and a plain `git clone` do not fetch submodule contents. WolvenKit is large; the first fetch takes a few minutes. If it stops with "Filename too long", run `git config --global core.longpaths true` and repeat |
| `dotnet restore` | Restores the bridge and the four WolvenKit projects it references. Needs network access the first time |
| `dotnet build` | Builds WolvenKit's `Core`, `RED4`, `Common` and `Modkit` from the submodule, then the bridge. About 45 s to a minute single-threaded. `-v q` hides the 394 nullability warnings from WolvenKit's RED4 project, which are expected. Output: `src\REDbridge\bin\Release\net10.0\REDbridge.exe` with about 200 MB beside it |
| `tools\probe.py` | Starts the bridge with the game folder, loads the archive indexes (the game does not need to be running) and calls every method: list and search, CR2W to JSON and back with a byte comparison, texture to PNG, hashing. About 5 to 8 s. Output goes to `probe-out`, which git ignores |

Do not build the WolvenKit projects on their own with paths like `WolvenKit.Modkit/WolvenKit.Modkit.csproj`; those paths exist only inside `external\WolvenKit`, and the bridge build covers them.

### .NET runtime location

With a machine-wide SDK (`winget install --id Microsoft.DotNet.SDK.10 -e`) nothing more is needed. With the per-user SDK from section 2, `REDbridge.exe` needs `DOTNET_ROOT` in the environment of whatever starts it, or it fails to find a runtime:

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
```

### Oodle

On first run the bridge copies the game's `oo2ext_7_win64.dll` to `%LOCALAPPDATA%` and loads it from there, as WolvenKit does, and logs "Using the game's Oodle library". Without it WolvenKit falls back to the bundled `kraken.dll` and warns that results may differ.

## 6. Docs site and brand images

The site at https://cyberbuildsai.github.io/REDbridge/ is built by `.github/workflows/pages.yml` on every push to `main`. **Settings > Pages > Source** must be **GitHub Actions**.

```powershell
.\tools\brand\render.ps1   # banner, social preview, icon, badges
```

| Output | Used by |
|---|---|
| `docs/assets/brand/banner.png` | README header |
| `docs/assets/brand/social.png` | Upload by hand in Settings > General > Social preview |
| `docs/assets/brand/icon.png` | Site and app icon |
| `docs/assets/brand/badges/*.svg` | README badges. Edit the list in `tools/brand/badges.py` |
