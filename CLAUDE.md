# REDbridge: notes for Claude sessions

REDbridge is the .NET process that hosts WolvenKit's headless libraries and exposes them over JSON-RPC for REDcode (https://github.com/CyberbuildsAI/REDcode) and for AI assistants through an MCP server. It is one of three repos by the same owner: REDnode (runtime scripting), REDcode (the IDE) and REDbridge (the engine host). REDnode's `CLAUDE.md` and `docs/dev-setup.md` set the shared conventions.

## Current status and next step

- **Design:** settled in the REDcode design document, which the owner holds privately. Ask for it before architectural work. Sections 05 (architecture) and the bridge protocol table are the spec for this repo.
- **In the repo:** docs site, brand tooling, no code yet.
- **Next step (C0):** install the .NET 10 SDK, clone WolvenKit at the pinned commit (`4d1716b1`, 9.1.0) with `core.longpaths` on, build `WolvenKit.Core`, `Common`, `RED4` and `Modkit`, and record what the bridge needs in `docs/dev-setup.md`. Then C1: a console app speaking JSON-RPC over stdio with `archive.list`, `cr2w.toJson`, `cr2w.fromJson`, `texture.toPng` and `hash.*`.

## The license boundary

- This repo is **GPL-3.0** because it links WolvenKit's GPL-3.0 libraries. REDcode is MIT.
- Never copy code from this repo into REDcode, and never let REDcode link the bridge in-process. The only contract between them is JSON-RPC over stdio.
- Never commit WolvenKit binaries, game files or data extracted from the game. Reference WolvenKit as a pinned source dependency.

## Conventions

- Commit straight to `main` and push, in small logical commits. Agree the plan in chat first. PRs only for large or risky changes.
- Git identity is set per repo to `CyberbuildsAI` with the GitHub no-reply email. Never change the global identity.
- Record every tool install in `docs/dev-setup.md`, with versions and exact commands.
- Writing style: plain, short sentences, tables for comparisons. Mark anything unverified as unverified.
- House style is the HUD look. `tools/brand/render.ps1` regenerates the banner, badges and icon; re-run it when versions in the badges change.

## Docs site

- `docs/` is published by GitHub Pages at https://cyberbuildsai.github.io/REDbridge/ through `.github/workflows/pages.yml`. Settings > Pages > Source must be GitHub Actions.
- Write docs as markdown in `docs/`. Jekyll renders them with the HUD layout. Add every new page to `nav` in `docs/_config.yml`.
- Plan a generated reference for the JSON-RPC methods once they exist; it should read the same as REDnode's API reference.
