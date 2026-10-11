"""Build offline emoji labels from pinned, locally reviewed Unicode CLDR snapshots.

Usage: python scripts/generate-emoji-languages.py SNAPSHOT_DIRECTORY
Inputs: {en,es}-{annotations,annotationsDerived}.json from cldr-json commit below.
No downloads at build time or application runtime.
"""
import hashlib
import json
from pathlib import Path
import sys

COMMIT = "91c267402229a59e3ef2774544f001bf959e8809"
root = Path(__file__).resolve().parents[1]
snapshot = Path(sys.argv[1])
items = json.loads((root / "src/SlashText/Assets/NotoEmoji/catalog.json").read_text(encoding="utf-8"))["Items"]
normalize = lambda value: value.replace("\ufe0f", "")
for language in ("en", "es"):
    annotations, hashes = {}, {}
    for kind in ("annotations", "annotationsDerived"):
        file = snapshot / f"{language}-{kind}.json"
        hashes[file.name] = hashlib.sha256(file.read_bytes()).hexdigest()
        document = json.loads(file.read_text(encoding="utf-8"))
        body = document.get("annotations", document.get("annotationsDerived"))
        annotations.update({normalize(value): record for value, record in body["annotations"].items()})
    labels, translated = {}, 0
    for item in items:
        value = normalize(item["Value"])
        annotation = annotations.get(value, {})
        name = annotation.get("tts", [])
        translated += bool(name)
        fallback = "Emoji " + " ".join(f"U+{ord(c):X}" for c in value)
        labels[value] = [name[0] if name else fallback, " ".join(annotation.get("default", []))]
    result = {"Commit": COMMIT, "InputHashes": hashes, "TranslatedCount": translated, "Labels": labels}
    target = root / f"src/SlashText/Localization/emoji-{language}.json"
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{language}: {translated}/{len(labels)} CLDR labels, remaining Unicode-code fallback")
