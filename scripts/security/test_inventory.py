import copy
import pathlib
import struct
import tempfile
import unittest
import zlib
from inventory import MARKER, bundle, content, cyclonedx, cache_index, build_inventory, sha
from unittest.mock import patch
import json


def fixture(path="x64/component.dll", payload=b"independent component bytes", compressed=True):
    prefix = b"MZ" + bytes(30)
    encoded = zlib.compress(payload)[2:-4] if compressed else payload
    header_offset = len(prefix) + len(encoded) + 8 + len(MARKER)
    # BinaryWriter length prefix for this deliberately short test path.
    header = struct.pack("<IIi", 6, 0, 1) + b"\x03test"[:4] + bytes(40)
    header += struct.pack("<qqqB", len(prefix), len(payload), len(encoded) if compressed else 0, 2)
    header += bytes([len(path)]) + path.encode()
    return prefix + encoded + struct.pack("<q", header_offset) + MARKER + header


class InventoryTests(unittest.TestCase):
    def test_real_format_bytes_round_trip(self):
        for compressed in (False, True):
            data = fixture(compressed=compressed)
            entries = bundle(data)
            self.assertEqual(entries[0]["path"], "x64/component.dll")
            self.assertEqual(content(data, entries[0]), b"independent component bytes")

    def test_unknown_format_and_missing_signature_fail(self):
        data = bytearray(fixture())
        offset = data.index(MARKER) + len(MARKER)
        data[offset] = 9
        with self.assertRaises(ValueError):
            bundle(data)
        with self.assertRaises(ValueError):
            bundle(b"not an executable")

    def test_paths_cannot_escape(self):
        for path in ("../bad", "x/../bad", "/bad", "C:/bad", "x//bad"):
            with self.assertRaises(ValueError):
                bundle(fixture(path=path))

    def test_truncation_and_wrong_length_fail(self):
        data = fixture()
        entry = bundle(data)[0]
        wrong = copy.deepcopy(entry)
        wrong["size"] += 1
        with self.assertRaises(ValueError):
            content(data, wrong)
        with self.assertRaises((ValueError, struct.error)):
            bundle(data[:-10])
        bad_offset = bytearray(data)
        struct.pack_into("<q", bad_offset, bad_offset.index(MARKER) - 8, len(data) + 1)
        with self.assertRaises(ValueError):
            bundle(bad_offset)

    def test_sbom_contains_measured_files_not_only_packages(self):
        inventory = dict(executable_sha256="a" * 64, source_commit="b" * 40,
                         packages={"Wrapper/2.0": dict(name="Wrapper", version="2.0", origin="https://example.org", license="NOASSERTION")},
                         bundle_files=[dict(path="x64/native.dll", sha256="c" * 64, sources=[])],
                         embedded_resources=[dict(name="model.traineddata", sha256="d" * 64, version="commit", license="Apache-2.0", origin="https://example.org/model")], frameworks=[])
        bom = cyclonedx(inventory)
        self.assertEqual(bom["specVersion"], "1.6")
        self.assertEqual(len(bom["components"]), 3)
        self.assertIn("x64/native.dll", [x["name"] for x in bom["components"]])
        self.assertEqual(bom["metadata"]["component"]["hashes"][0]["content"], "a" * 64)

    def test_package_match_uses_binary_hash_and_preserves_license(self):
        import json
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            package = root / "pkg/1.0"
            package.mkdir(parents=True)
            (package / "different-name.dll").write_bytes(b"actual binary")
            (package / "pkg.nuspec").write_text('<package><metadata><license type="expression">MIT</license><projectUrl>https://example.org/pkg</projectUrl></metadata></package>')
            assets = root / "assets.json"
            assets.write_text(json.dumps(dict(packageFolders={str(root): {}}, libraries={"Pkg/1.0": dict(type="package", path="pkg/1.0", files=["different-name.dll"])})))
            index, packages = cache_index(assets)
            self.assertEqual(next(iter(index.values()))[0]["package"], "Pkg/1.0")
            self.assertEqual(packages["Pkg/1.0"]["license"], "MIT")

    def test_published_assets_cannot_claim_stale_provenance(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            for folder in ("src/SlashText/obj", "src/SlashText/Assets/NotoEmoji", "scripts/security", "published"):
                (root / folder).mkdir(parents=True)
            (root / "src/SlashText/obj/project.assets.json").write_text(json.dumps(dict(packageFolders={}, libraries={})))
            (root / "scripts/security/native-versions.json").write_text('{}')
            (root / "scripts/restore-ocr-models.ps1").write_text('')
            catalog = dict(Commit="b" * 40, Items=[dict(AssetName="one.png", SourcePath="2D/png/128/one.png", BlobSha="a" * 40)])
            catalog_bytes = json.dumps(catalog).encode()
            (root / "src/SlashText/Assets/NotoEmoji/catalog.json").write_bytes(catalog_bytes)
            executable = root / "published/SlashDesk.exe"
            executable.write_bytes(b"fixture apphost")
            (executable.parent / "SlashDesk.deps.json").write_text('{"libraries":{}}')
            (executable.parent / "SlashDesk.runtimeconfig.json").write_text('{"runtimeOptions":{"includedFrameworks":[]}}')
            resources = dict(entries=[dict(name="SlashText.Assets.NotoEmoji.catalog.json", sha256=sha(catalog_bytes)),
                                      dict(name="SlashText.Assets.NotoEmoji.catalog-assets.zip!/one.png", sha256="c" * 64, gitBlobSha1="a" * 40)])
            with patch("inventory.subprocess.check_output", return_value="d" * 40):
                result = build_inventory(executable, root, resources, "directory")
                self.assertEqual(len(result["embedded_resources"]), 2)
                stale_catalog = copy.deepcopy(resources)
                stale_catalog["entries"][0]["sha256"] = "0" * 64
                with self.assertRaisesRegex(ValueError, "catalog differs"):
                    build_inventory(executable, root, stale_catalog, "directory")
                stale_png = copy.deepcopy(resources)
                stale_png["entries"][1]["gitBlobSha1"] = "0" * 40
                with self.assertRaisesRegex(ValueError, "Git blob"):
                    build_inventory(executable, root, stale_png, "directory")
                missing_png = copy.deepcopy(resources)
                missing_png["entries"].pop()
                with self.assertRaisesRegex(ValueError, "set differs"):
                    build_inventory(executable, root, missing_png, "directory")


if __name__ == "__main__":
    unittest.main()
