"""Exercises the bridge over its real protocol: JSON-RPC 2.0, Content-Length framed, on stdio.

    python -I tools/probe.py <path to REDbridge.exe or .dll> "<Cyberpunk 2077 folder>" [out dir]
    npm run probe -- "<Cyberpunk 2077 folder>"        (same thing, using the Release build)

Standard library only. The bridge's stderr log is passed through.
"""
import json
import os
import subprocess
import sys
import time


class Bridge:
    def __init__(self, exe, game):
        exe = os.path.abspath(exe)
        cmd = ([exe] if exe.lower().endswith(".exe") else ["dotnet", exe]) + ["--game", game]
        self.p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=sys.stderr)
        self.n = 0

    def call(self, method, **params):
        self.n += 1
        body = json.dumps({"jsonrpc": "2.0", "id": self.n, "method": method, "params": params}).encode()
        self.p.stdin.write(f"Content-Length: {len(body)}\r\n\r\n".encode() + body)
        self.p.stdin.flush()
        headers = {}
        while True:
            line = self.p.stdout.readline()
            if not line or line == b"\r\n":
                break
            k, v = line.decode().split(":", 1)
            headers[k.strip().lower()] = v.strip()
        msg = json.loads(self.p.stdout.read(int(headers["content-length"])))
        if "error" in msg:
            raise RuntimeError(f"{method}: {msg['error'].get('message')}")
        return msg["result"]

    def close(self):
        self.p.stdin.close()
        self.p.wait(timeout=30)


def main():
    exe, game = sys.argv[1], sys.argv[2]
    out = sys.argv[3] if len(sys.argv) > 3 else os.path.join(os.getcwd(), "probe-out")
    b = Bridge(exe, game)
    t = time.time()

    def step(name, fn):
        s = time.time()
        r = fn()
        print(f"[{time.time() - s:6.2f}s] {name}: {r}")
        return r

    info = step("bridge.info", lambda: b.call("bridge.info"))
    assert info["gameLoaded"], "game not loaded"
    archives = step("archive.archives", lambda: [(a["name"], a["files"]) for a in b.call("archive.archives")][:4])
    page = step("archive.list base/characters", lambda: b.call("archive.list", prefix="base\\characters", limit=3))
    mods_dir = os.path.join(game, "archive", "pc", "mod")
    mod_archives = [f for f in (os.listdir(mods_dir) if os.path.isdir(mods_dir) else []) if f.lower().endswith(".archive")]
    if mod_archives:
        opened = step("archive.openMod " + mod_archives[0], lambda: b.call("archive.openMod", path=os.path.join(mods_dir, mod_archives[0]))["files"])
        print("   first:", [i["name"] for i in b.call("archive.openMod", path=os.path.join(mods_dir, mod_archives[0]))["items"][:3]])
    else:
        print("   archive.openMod: no mod archives in archive/pc/mod, skipped")
    print("   first:", [i["name"] for i in page["items"]])
    hits = step("archive.search .xbm", lambda: b.call("archive.search", query=".xbm", limit=5))
    step("hash.fnv1a64", lambda: b.call("hash.fnv1a64", value="base\\characters"))

    ent = b.call("archive.list", prefix="base\\characters", limit=50)["items"]
    ent = next((i for i in ent if i["name"].endswith(".ent")), None) or next(iter(ent))
    j = step(f"cr2w.toJson {ent['name']}", lambda: b.call("cr2w.toJson", path=ent["name"], outFile=os.path.join(out, "file.json")))
    step("hash.resolve", lambda: b.call("hash.resolve", hash=ent["hash"]))
    step("cr2w.fromJson (round trip)", lambda: b.call("cr2w.fromJson", jsonFile=j["file"], outFile=os.path.join(out, "file.roundtrip.bin")))
    orig = step("archive.extract (original)", lambda: b.call("archive.extract", path=ent["name"], outFile=os.path.join(out, "file.original.bin")))
    print("   round trip bytes equal:", os.path.getsize(j["file"].replace("file.json", "file.roundtrip.bin")) == orig["bytes"])

    xbm = next((i for i in hits["items"] if i["name"].endswith(".xbm")), None)
    if xbm:
        step(f"texture.toPng {xbm['name']}", lambda: b.call("texture.toPng", path=xbm["name"], outFile=os.path.join(out, "texture.png")))

    b.close()
    print(f"done in {time.time() - t:.1f}s, outputs in {out}")


if __name__ == "__main__":
    main()
