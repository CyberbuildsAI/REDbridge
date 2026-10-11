# Development setup

Everything needed to work on REDbridge, in order. A fresh machine should be able to follow this top to bottom. The shared tools (Git, Python, winget) are the same as in [REDnode's setup guide](https://cyberbuildsai.github.io/REDnode/dev-setup.html).

Reference machine: Windows 11 Home (build 26200), 16 GB RAM.

## 1. Base tools

| Tool | Version used | Why | Install |
|---|---|---|---|
| Git for Windows | 2.55.0 | Source control | `winget install --id Git.Git -e` |
| Python | 3.14.6 | Brand tooling | Any Python 3.10 or later |
| Chrome or Edge | any | Renders the brand images | Already present on Windows |

Git needs long paths on Windows, because WolvenKit's generated type files exceed the default limit:

```powershell
git config --global core.longpaths true
```

## 2. .NET toolchain

WolvenKit targets .NET 10 (`global.json` asks for SDK 10.0.302 or a later feature band).

| Tool | Version | Install |
|---|---|---|
| .NET 10 SDK | 10.0.3xx | `winget install --id Microsoft.DotNet.SDK.10 -e` |

Status on the reference machine: **not installed yet, unverified.**

## 3. WolvenKit source

REDbridge builds against WolvenKit's headless libraries at a pinned commit. How that dependency is wired (submodule, subtree or package feed) is decided in C0.

```powershell
git clone https://github.com/WolvenKit/WolvenKit.git
git -C WolvenKit checkout 4d1716b1
```

Headless projects: `WolvenKit.Core`, `WolvenKit.Common`, `WolvenKit.RED4`, `WolvenKit.Modkit`. The WPF projects (`WolvenKit`, `WolvenKit.App`) are not needed.

## 4. Repository and GitHub

```powershell
git clone https://github.com/CyberbuildsAI/REDbridge.git
cd REDbridge
git config user.name  "CyberbuildsAI"
git config user.email "340969962+CyberbuildsAI@users.noreply.github.com"
```

## 5. Docs site and brand images

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
