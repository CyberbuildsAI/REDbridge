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
| C3 Edit | Started: `texture.fileToPng` for REDcode's texture viewer. Next: `cr2w.getChunk` and `cr2w.patch` for the CR2W editor |
| C4 to C5 | Not started |

Source code and issues: [github.com/CyberbuildsAI/REDbridge](https://github.com/CyberbuildsAI/REDbridge)
