"""Validate the Xeri Game package setup and Unity metadata."""

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "com.inonego.xeri.game"


def check():
    package = json.loads((PACKAGE / "package.json").read_text(encoding="utf-8-sig"))
    assert package["name"] == "com.inonego.xeri.game"
    assert package["dependencies"]["com.inonego.xeri"] == "0.0.1"
    runtime = json.loads(
        (PACKAGE / "Runtime/inonego.Xeri.Game.asmdef").read_text(encoding="utf-8-sig")
    )
    assert runtime["name"] == "inonego.Xeri.Game"
    assert runtime["rootNamespace"] == "inonego.Xeri.Game"
    assert runtime["references"] == ["inonego.Xeri"]

    files = list(PACKAGE.rglob("*"))
    seen = set()
    for meta in (p for p in files if p.is_file() and p.suffix == ".meta"):
        match = re.search(
            r"(?m)^guid:\s*([a-fA-F0-9]{32})\s*$",
            meta.read_text(encoding="utf-8-sig"),
        )
        assert match, f"Missing Unity GUID: {meta}"
        guid = match.group(1).lower()
        assert guid not in seen, f"Duplicate Unity GUID: {meta}"
        seen.add(guid)

    for asset in (p for p in files if p.is_file() and p.suffix != ".meta"):
        assert asset.with_name(asset.name + ".meta").exists(), f"Missing .meta: {asset}"
    for folder in (p for p in files if p.is_dir()):
        assert folder.with_name(folder.name + ".meta").exists(), f"Missing folder .meta: {folder}"

    print(f"OK: {package['name']}, Unity GUIDs={len(seen)}")


if __name__ == "__main__":
    try:
        check()
    except (AssertionError, OSError, ValueError, KeyError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        sys.exit(1)