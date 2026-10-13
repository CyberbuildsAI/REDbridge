# RED<span class="y">bridge</span> docs

REDbridge hosts WolvenKit's engine in a local process and exposes it over JSON-RPC to REDcode and to AI assistants. These pages track its setup and, once it exists, its protocol.

| Document | What it covers |
|---|---|
| [Dev setup](dev-setup.md) | Every tool and step needed to build REDbridge on a new machine |

The architecture lives in the REDcode design document, which is not published yet.

## Roadmap status

| Phase | Status |
|---|---|
| C0 Decide | Done. WolvenKit's headless libraries build on the reference machine; findings in Dev setup |
| C1 Bridge MVP | Done. The bridge answers every planned method over stdio; verified against a game install with `tools/probe.py` |
| C2 Browse | Done on the bridge side: `archive.tree` (folder index over 583,798 files, built in about 1 s), `archive.extractMany`, `cr2w.fileToJson` |
| C3 Edit | In progress. `texture.fileToPng` for the texture viewer. Document sessions for the CR2W editor (0.5.0): `cr2w.open` parses once and keeps the file in memory, `cr2w.getNode` reads one node with arrays paged (60,303 localization rows open in about 0.4 s and page in under 1 ms), `cr2w.patch` applies JSON Patch operations atomically and returns the inverse for undo, `cr2w.save` writes through WolvenKit's writer, `cr2w.describeType` and `cr2w.createInstance` drive the property grid, `cr2w.search` finds values and property names (60,303 rows in about 0.2 s), `cr2w.getNodes` reads several nodes in one call |
| For REDdeck | Done: `archive.openMod` (0.4.0) lists the resources inside any `.archive` on disk without loading a game, so the mod manager can show conflicts between mod archives |
| C4 to C5 | Not started |

Source code and issues: [github.com/Edgemodders/REDbridge](https://github.com/Edgemodders/REDbridge)
