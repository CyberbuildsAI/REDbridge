// Fetches WolvenKit's source at the version REDbridge builds against into external/WolvenKit, for
// reading: the build does not use it, since REDbridge references WolvenKit's NuGet packages.
//
//   npm run wolvenkit:source
//
// The version is WolvenKitVersion in src/REDbridge/REDbridge.csproj; WolvenKit tags each release with
// it. A first run clones that tag (shallow); later runs move an existing clone to it. git ignores the
// folder.
import { execFileSync } from "node:child_process";
import * as fs from "node:fs";
import * as path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const csproj = fs.readFileSync(path.join(root, "src", "REDbridge", "REDbridge.csproj"), "utf8");
const version = /<WolvenKitVersion>([^<]+)<\/WolvenKitVersion>/.exec(csproj)?.[1];
if (!version) {
  console.error("No WolvenKitVersion in src/REDbridge/REDbridge.csproj");
  process.exit(1);
}
const dir = path.join(root, "external", "WolvenKit");
// WolvenKit's paths outgrow Windows' 260-character limit; longpaths lets git write them.
const git = (...args) => execFileSync("git", ["-c", "core.longpaths=true", ...args], { stdio: "inherit" });

if (fs.existsSync(path.join(dir, ".git"))) {
  git("-C", dir, "fetch", "--depth", "1", "origin", "tag", version);
  git("-C", dir, "checkout", "--detach", version);
} else {
  fs.mkdirSync(path.dirname(dir), { recursive: true });
  git("clone", "--depth", "1", "--branch", version, "https://github.com/WolvenKit/WolvenKit.git", dir);
}
console.log(`WolvenKit ${version} source is in ${path.relative(root, dir)}`);
