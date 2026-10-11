// Runs `dotnet` with the arguments given, finding an SDK the way the setup guide installs one:
//   1. DOTNET_ROOT, when set
//   2. the per-user install in %LOCALAPPDATA%\Microsoft\dotnet, when it has an sdk folder
//   3. whatever `dotnet` is on PATH
// The machine-wide dotnet.exe in Program Files is often a runtime only; with no SDK beside it
// a build fails with "No .NET SDKs were found". This avoids that without touching PATH.
import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { join } from "node:path";

const candidates = [
  process.env.DOTNET_ROOT,
  process.env.LOCALAPPDATA && join(process.env.LOCALAPPDATA, "Microsoft", "dotnet"),
].filter(Boolean);

let exe = "dotnet";
let root;
for (const dir of candidates) {
  const candidate = join(dir, process.platform === "win32" ? "dotnet.exe" : "dotnet");
  if (existsSync(candidate) && existsSync(join(dir, "sdk"))) {
    exe = candidate;
    root = dir;
    break;
  }
}

const env = { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: "1", DOTNET_NOLOGO: "1" };
if (root) {
  env.DOTNET_ROOT = root;
}
const result = spawnSync(exe, process.argv.slice(2), { stdio: "inherit", env });
process.exit(result.status ?? 1);
