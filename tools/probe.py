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

    # mesh.info and sector.build, for EDGEbuilder: a crate mesh, a crate entity and a light baked into a sector.
    crates = b.call("archive.search", query="cargo_crate", limit=200)["items"]
    mesh = next((i for i in crates if i["name"].endswith(".mesh")), None)
    ent = next((i for i in crates if i["name"].endswith(".ent")), None)
    if mesh and ent:
        mi = step(f"mesh.info {mesh['name']}", lambda: b.call("mesh.info", path=mesh["name"]))
        assert mi["type"] == "mesh" and mi["bounds"] and len(mi["bounds"]["min"]) == 3, mi
        ei = step(f"mesh.info {ent['name']}", lambda: b.call("mesh.info", path=ent["name"]))
        assert ei["type"] == "entity", ei
        print("   entity parts:", [(p["mesh"].split("\\")[-1], p["bounds"] is not None) for p in ei["parts"]][:4], "appearances:", ei["appearances"][:4])
        many = step("mesh.infoMany", lambda: b.call("mesh.infoMany", paths=[mesh["name"], ent["name"], "base\\nope.mesh"]))
        assert many[2]["error"] and not many[0]["error"], many
        scene = {
            "format": 1, "name": "Probe Scene", "game": "2.31",
            "objects": [
                {"id": "crate_a", "kind": "mesh", "resource": mesh["name"], "appearance": (mi["appearances"] or ["default"])[0],
                 "transform": {"position": [-1200.0, 1500.0, 20.0], "rotation": [0, 0, 0.3826834, 0.9238795], "scale": [1, 1, 1]}},
                {"id": "crate_b", "kind": "entity", "resource": ent["name"],
                 "transform": {"position": [-1198.0, 1500.0, 20.0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1]}, "streaming": {"primary": 120, "secondary": 100}},
                {"id": "lamp", "kind": "light", "props": {"type": "spot", "color": [255, 200, 120], "intensity": 800, "radius": 12, "outerAngle": 60},
                 "transform": {"position": [-1199.0, 1500.0, 23.0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1]}},
            ],
        }
        project = os.path.join(out, "project")
        os.makedirs(project, exist_ok=True)
        built = step("sector.build", lambda: b.call("sector.build", project=project, scene=scene))
        assert built["nodes"] == 3 and os.path.exists(built["sector"]) and os.path.exists(built["block"]) and os.path.exists(built["xl"]), built
        back = step("cr2w.fileToJson (the sector reads back)", lambda: b.call("cr2w.fileToJson", file=built["sector"]))
        root = json.loads(back["json"])["Data"]["RootChunk"]
        kinds = [n["Data"]["$type"] for n in root["nodes"]]
        data = root["nodeData"]["Data"]
        assert kinds == ["worldMeshNode", "worldEntityNode", "worldStaticLightNode"], kinds
        assert len(data) == 3 and data[1]["UkFloat1"] == 120 and data[1]["MaxStreamingDistance"] == 100, data[1]
        assert data[0]["QuestPrefabRefHash"]["$value"] == "edgebuilder/probe_scene/crate_a", data[0]["QuestPrefabRefHash"]
        print("   sector version", root["version"], "nodes", kinds, "bounds of crate_a", data[0]["Bounds"]["Min"], data[0]["Bounds"]["Max"])
        blockBack = step("cr2w.fileToJson (the block reads back)", lambda: b.call("cr2w.fileToJson", file=built["block"]))
        desc = json.loads(blockBack["json"])["Data"]["RootChunk"]["descriptors"]
        assert len(desc) == 1 and desc[0]["data"]["DepotPath"]["$value"].endswith("probe_scene.streamingsector"), desc
        print("   streaming box", built["streamingBox"], "xl:", open(built["xl"], encoding="utf-8").read().strip().splitlines()[-3:])
        # Validation refuses a bad scene and writes nothing.
        try:
            b.call("sector.build", project=project, name="bad", scene={"format": 1, "objects": [{"id": "x", "kind": "mesh", "resource": "base\\nope.mesh"}, {"id": "x", "kind": "thing"}]})
            raise AssertionError("a bad scene was accepted")
        except RuntimeError as e:
            print("   bad scene refused:", str(e).splitlines()[0][:100], "...")
            assert not os.path.exists(os.path.join(project, "source", "archive", "edgebuilder", "bad"))
    else:
        print("   mesh.info / sector.build: no cargo_crate mesh and entity found, skipped")

    b.close()
    print(f"done in {time.time() - t:.1f}s, outputs in {out}")


if __name__ == "__main__":
    main()
