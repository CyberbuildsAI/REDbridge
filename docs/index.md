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
| C2 to C5 | Not started. Next is the REDcode extension that spawns the bridge |

Source code and issues: [github.com/CyberbuildsAI/REDbridge](https://github.com/CyberbuildsAI/REDbridge)
