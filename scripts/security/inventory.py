"""Inspect actual .NET single-file v6 packages and emit a CycloneDX inventory.

Only Python's standard library; source declarations never imply that an asset
was shipped. Match bundled bytes against resolved NuGet cache files by SHA-256.
Unknown origins/licenses stay explicit for #96/#97 instead of being guessed.
"""
import argparse
import hashlib
import json
import pathlib
import re
import struct
import subprocess
import tempfile
import uuid
import xml.etree.ElementTree as ET
import zlib
from urllib.parse import quote

MARKER = bytes.fromhex("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae")
LIMIT = 512 * 1024 * 1024


def sha(data):
    return hashlib.sha256(data).hexdigest()


def bundle(data):
    position = data.find(MARKER)
    if position < 8:
        raise ValueError("No .NET bundle signature")
    cursor = struct.unpack_from("<q", data, position - 8)[0]
    if not 0 < cursor < len(data):
        raise ValueError("Invalid bundle header offset")

    def unpack(fmt):
        nonlocal cursor
        value = struct.unpack_from(fmt, data, cursor)
        cursor += struct.calcsize(fmt)
        return value

    def string():
        nonlocal cursor
        size = 0
        for shift in range(0, 35, 7):
            byte = unpack("<B")[0]
            size |= (byte & 127) << shift
            if not byte & 128:
                break
        else:
            raise ValueError("Invalid string length")
        if size > 4096 or cursor + size > len(data):
            raise ValueError("Invalid path length")
        value = data[cursor:cursor + size].decode("utf-8")
        cursor += size
        return value

    major, minor, count = unpack("<IIi")
    if (major, minor) != (6, 0) or not 1 <= count <= 10000:
        raise ValueError("Only reviewed bundle format 6.0 is supported")
    string()  # Bundle ID is not a component version.
    unpack("<qqqqQ")
    entries = []
    names = set()
    for _ in range(count):
        offset, size, compressed, kind = unpack("<qqqB")
        path = string().replace("\\", "/")
        if path in names or path.startswith("/") or ":" in path or any(x in ("", ".", "..") for x in path.split("/")):
            raise ValueError("Duplicate/unsafe bundle path")
        names.add(path)
        if min(offset, size, compressed) < 0 or size > LIMIT or offset + (compressed or size) > len(data):
            raise ValueError("Bundle entry out of bounds")
        entries.append(dict(path=path, offset=offset, size=size, compressed=compressed, kind=kind))
    return entries


def content(data, entry):
    value = data[entry["offset"]:entry["offset"] + (entry["compressed"] or entry["size"])]
    if entry["compressed"]:
        inflater = zlib.decompressobj(-15)
        value = inflater.decompress(value, entry["size"] + 1)
        if not inflater.eof or inflater.unused_data or inflater.unconsumed_tail:
            raise ValueError("Invalid compressed bundle entry")
    if len(value) != entry["size"]:
        raise ValueError("Bundle entry size differs")
    return value


def license_metadata(folder):
    specs = list(folder.glob("*.nuspec"))
    if len(specs) != 1:
        return {"license": "NOASSERTION", "origin": "https://www.nuget.org"}
    tree = ET.parse(specs[0])
    values = {e.tag.split("}")[-1]: e for e in tree.iter()}
    license_node = values.get("license")
    license_value = license_node.text if license_node is not None else "NOASSERTION"
    origin_node = values.get("projectUrl")
    return {"license": license_value or "NOASSERTION",
            "origin": origin_node.text if origin_node is not None else "https://www.nuget.org"}


def cache_index(assets_path):
    assets = json.loads(assets_path.read_text(encoding="utf-8-sig"))
    index, packages = {}, {}
    for identifier, library in assets["libraries"].items():
        if library["type"] != "package":
            continue
        name, version = identifier.rsplit("/", 1)
        matches = [pathlib.Path(p) / library["path"] for p in assets["packageFolders"]
                   if (pathlib.Path(p) / library["path"]).is_dir()]
        if not matches:
            raise ValueError("Resolved NuGet package missing from cache: " + identifier)
        folder = matches[0]
        packages[identifier] = dict(name=name, version=version, **license_metadata(folder))
        for relative in library["files"]:
            path = folder / relative
            if path.is_file() and path.suffix.lower() in (".dll", ".exe", ".json", ".txt", ".md"):
                digest = sha(path.read_bytes())
                index.setdefault(digest, []).append(dict(package=identifier, path=relative))
    return index, packages


def build_inventory(executable, root, resources, layout="single-file"):
    data = executable.read_bytes()
    if layout == "single-file":
        entries = bundle(data)
        by_name = {e["path"]: e for e in entries}
        read_content = lambda e: content(data, e)
    else:
        entries = [dict(path=p.relative_to(executable.parent).as_posix(), size=p.stat().st_size,
                        kind="directory-file") for p in sorted(executable.parent.rglob("*")) if p.is_file()]
        by_name = {e["path"]: e for e in entries}
        read_content = lambda e: (executable.parent / e["path"]).read_bytes()
    deps = json.loads(read_content(by_name["SlashDesk.deps.json"]))
    runtime = json.loads(read_content(by_name["SlashDesk.runtimeconfig.json"]))
    index, packages = cache_index(root / "src/SlashText/obj/project.assets.json")
    # Runtime packs are not listed as ordinary PackageReferences, but their
    # bytes are shipped in a self-contained build and must be matched too.
    assets = json.loads((root / "src/SlashText/obj/project.assets.json").read_text(encoding="utf-8-sig"))
    for framework in runtime["runtimeOptions"].get("includedFrameworks", []):
        name, version = framework["name"] + ".Runtime.win-x64", framework["version"]
        folders = [pathlib.Path(p) / name.lower() / version for p in assets["packageFolders"]]
        folder = next((f for f in folders if f.is_dir()), None)
        if folder is None:
            raise ValueError("Published runtime pack missing from restored cache: " + name)
        identifier = name + "/" + version
        packages[identifier] = dict(name=name, version=version, **license_metadata(folder))
        for p in folder.rglob("*"):
            if p.is_file() and p.suffix.lower() in (".dll", ".exe"):
                index.setdefault(sha(p.read_bytes()), []).append(dict(package=identifier, path=p.relative_to(folder).as_posix()))
    native_versions = json.loads((root / "scripts/security/native-versions.json").read_text())
    files, used = [], set()
    for entry in entries:
        digest = sha(read_content(entry))
        candidates = index.get(digest, [])
        used.update(c["package"] for c in candidates)
        files.append(dict(path=entry["path"], size=entry["size"], sha256=digest, kind=entry["kind"],
                          sources=candidates, native_evidence=native_versions.get(digest),
                          version_source="resolved package/hash" if candidates else "published bytes; origin unresolved"))
    catalog_path = root / "src/SlashText/Assets/NotoEmoji/catalog.json"
    catalog = json.loads(catalog_path.read_text(encoding="utf-8-sig"))
    embedded_catalog = next(r for r in resources["entries"] if r["name"] == "SlashText.Assets.NotoEmoji.catalog.json")
    if embedded_catalog["sha256"] != sha(catalog_path.read_bytes()):
        raise ValueError("Published emoji catalog differs from source; do not infer stale provenance")
    noto = {i["AssetName"]: i for i in catalog["Items"]}
    shipped_emoji = {r["name"].split("!/")[1] for r in resources["entries"] if "catalog-assets.zip!/" in r["name"]}
    if shipped_emoji != set(noto):
        raise ValueError("Published emoji set differs from catalog")
    restore_script = (root / "scripts/restore-ocr-models.ps1").read_text(encoding="utf-8-sig")
    model_sets = {}
    for repository, model_commit, model_hashes in re.findall(
            r"Repository = '(tessdata_(?:best|fast))'; Commit = '([0-9a-f]{40})';.*?Models = @\{(.*?)\}", restore_script, re.S):
        model_sets[repository] = (model_commit, dict(re.findall(r"(eng|por) = '([0-9a-f]{64})'", model_hashes)))
    commit = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
    asset_entries = []
    for resource in resources["entries"]:
        name = resource["name"]
        origin, version, license_value = "repository embedded resource or compiled WPF asset", commit, "NOASSERTION"
        if "catalog-assets.zip!/" in name:
            item = noto[name.split("!/")[1]]
            if resource["gitBlobSha1"] != item["BlobSha"]:
                raise ValueError("Published emoji differs from catalog Git blob")
            origin = f'https://github.com/googlefonts/noto-emoji/blob/{catalog["Commit"]}/{item["SourcePath"]}'
            version = catalog["Commit"]
            license_value = "see FLAGS-LICENSE.txt" if item["SourcePath"].startswith("third_party/") else "Apache-2.0"
        elif name.endswith(".traineddata"):
            fast = ".Fast." in name
            repository = "tessdata_fast" if fast else "tessdata_best"
            version, expected_hashes = model_sets[repository]
            language = name.split(".")[-2]
            source_model = root / "src/SlashText/Assets/Ocr" / ("Fast" if fast else "") / (language + ".traineddata")
            if resource["sha256"] != expected_hashes[language] or resource["sha256"] != sha(source_model.read_bytes()):
                raise ValueError("Published OCR model differs from restored source")
            origin = f'https://github.com/tesseract-ocr/{repository}/blob/{version}/{language}.traineddata'
            license_value = "Apache-2.0"
        asset_entries.append(dict(**resource, origin=origin, version=version, license=license_value))
    return dict(schema_version=1, source_commit=commit, layout=layout, executable=executable.name, executable_sha256=sha(data),
                executed=False, frameworks=runtime["runtimeOptions"].get("includedFrameworks", []),
                deps_libraries=deps["libraries"], packages={p: packages[p] for p in sorted(used)},
                bundle_files=files, embedded_resources=asset_entries,
                limitations=["NuGet version is not native upstream version; OCR native review tracked by issue #97.",
                             "Unresolved origin/license is explicit, tracked by issue #96; no inferred blanket license.",
                             "Bundle/resources inspected without executing app/native code. WPF .g.resources includes compiled UI assets."])


def cyclonedx(inventory):
    components = []
    for framework in inventory["frameworks"]:
        c = dict(type="framework", name=framework["name"], version=framework["version"],
                 externalReferences=[dict(type="website", url="https://dotnet.microsoft.com/download/dotnet/10.0")],
                 licenses=[dict(license=dict(name="see runtime-pack license and third-party notices"))])
        c["bom-ref"] = "framework:" + framework["name"]
        components.append(c)
    for identifier, p in inventory["packages"].items():
        c = dict(type="library", name=p["name"], version=p["version"],
                 purl=f'pkg:nuget/{quote(p["name"])}@{quote(p["version"])}',
                 externalReferences=[dict(type="website", url=p["origin"])],
                 licenses=[dict(license=dict(name=p["license"]))])
        c["bom-ref"] = "nuget:" + identifier
        components.append(c)
    for f in inventory["bundle_files"]:
        c = dict(type="file", name=f["path"], hashes=[dict(alg="SHA-256", content=f["sha256"])],
                 properties=[dict(name="slashdesk:source-candidates", value=json.dumps(f["sources"]))])
        c["bom-ref"] = "bundle:" + f["path"]
        if f.get("native_evidence"):
            c["version"] = f["native_evidence"]["version"]
            c["properties"].append(dict(name="slashdesk:native-evidence", value=json.dumps(f["native_evidence"])))
        components.append(c)
    for f in inventory["embedded_resources"]:
        c = dict(type="file", name=f["name"], version=f["version"], hashes=[dict(alg="SHA-256", content=f["sha256"])],
                 licenses=[dict(license=dict(name=f["license"]))],
                 properties=[dict(name="slashdesk:origin", value=f["origin"])])
        c["bom-ref"] = "resource:" + f["name"]
        components.append(c)
    app = dict(type="application", name="SlashDesk", hashes=[dict(alg="SHA-256", content=inventory["executable_sha256"])])
    app["bom-ref"] = "application:SlashDesk"
    # The measured executable contains these items; this is a composition
    # relationship, not a claim of a complete native transitive dependency graph.
    dependencies = [{"ref": app["bom-ref"], "dependsOn": [c["bom-ref"] for c in components]}]
    return dict(bomFormat="CycloneDX", specVersion="1.6", serialNumber="urn:uuid:" + str(uuid.uuid4()), version=1,
                metadata=dict(component=app, properties=[dict(name="slashdesk:source-commit", value=inventory["source_commit"])]),
                components=components, dependencies=dependencies)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--executable", type=pathlib.Path, required=True)
    parser.add_argument("--root", type=pathlib.Path, required=True)
    parser.add_argument("--resource-tool", type=pathlib.Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--layout", choices=["single-file", "directory"], default="single-file")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    data = args.executable.read_bytes()
    with tempfile.TemporaryDirectory(prefix="SlashDesk inventory ") as tmp:
        assembly, report = pathlib.Path(tmp) / "app.dll", pathlib.Path(tmp) / "resources.json"
        if args.layout == "single-file":
            app_entry = next(e for e in bundle(data) if e["path"] == "SlashDesk.dll")
            assembly.write_bytes(content(data, app_entry))
        else:
            assembly = args.executable.parent / "SlashDesk.dll"
        subprocess.run([args.dotnet, str(args.resource_tool.resolve()), str(assembly), str(report)], check=True)
        inventory = build_inventory(args.executable, args.root, json.loads(report.read_text(encoding="utf-8-sig")), args.layout)
    args.output.mkdir(parents=True, exist_ok=True)
    for name, document in [("inventory.json", inventory), ("sbom.cdx.json", cyclonedx(inventory))]:
        (args.output / name).write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f'Inventory: {len(inventory["packages"])} shipped package origins; {len(inventory["bundle_files"])} bundle files; '
          f'{len(inventory["embedded_resources"])} resources/assets; executable SHA-256 {inventory["executable_sha256"]}')


if __name__ == "__main__":
    main()
