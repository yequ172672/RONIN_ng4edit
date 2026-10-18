"""Produce native-only raw NG4MOD v2. Does not load DLLs or install anything."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import tempfile


def require(condition, message):
    if not condition:
        raise ValueError(message)


def nonblank(value):
    return isinstance(value, str) and bool(value.strip())


def pe_check(path):
    """Basic header check only; not a PE loader or export/dependency validator."""
    size = path.stat().st_size
    require(0 < size <= 256 * 1024 * 1024, f"Invalid DLL size: {path}")
    with path.open("rb") as stream:
        dos = stream.read(64)
        require(len(dos) == 64 and dos[:2] == b"MZ", f"Not a DOS/PE file: {path}")
        offset = struct.unpack_from("<I", dos, 60)[0]
        require(64 <= offset <= size - 26, f"Invalid PE offset: {path}")
        stream.seek(offset)
        header = stream.read(26)
        require(header[:4] == b"PE\0\0", f"Missing PE signature: {path}")
        require(struct.unpack_from("<H", header, 4)[0] == 0x8664,
                f"DLL must target Windows x64: {path}")
        optional_size, flags, magic = struct.unpack_from("<HHH", header, 20)
        require(optional_size >= 112 and offset + 24 + optional_size <= size,
                f"Invalid optional header: {path}")
        require(flags & 0x2000 and magic == 0x20B, f"Not a PE32+ DLL: {path}")
    return size


def prepare(spec, base):
    require(isinstance(spec, dict), "Spec must be an object")
    allowed = {"mod_id", "name", "version", "author", "description", "dependencies",
               "native_libraries"}
    require(not spec.keys() - allowed, "Unsupported spec fields (native-only tool)")
    for key in ("mod_id", "name", "version"):
        require(nonblank(spec.get(key)), f"Missing or blank {key}")
    for key in ("author", "description"):
        require(isinstance(spec.get(key, ""), str), f"{key} must be a string")
    dependencies = spec.get("dependencies", [])
    require(isinstance(dependencies, list) and all(map(nonblank, dependencies)),
            "Dependencies must be an array of nonblank IDs")
    libraries = spec.get("native_libraries")
    require(isinstance(libraries, list) and 1 <= len(libraries) <= 64,
            "Expected 1..64 native libraries")
    names, claims, sources = set(), set(), []
    has_entry = False
    for item in libraries:
        require(isinstance(item, dict), "Native library must be an object")
        require(not item.keys() - {"source", "name", "load", "exclusive_claims"},
                "Unsupported native library fields")
        name = item.get("name")
        require(nonblank(name) and name.lower().endswith(".dll") and
                not any(c in name for c in '/\\:<>"|?*') and
                not any(ord(c) < 32 for c in name) and name == name.strip(),
                "Native name must be a safe plain .dll filename")
        require(name.lower() not in names, f"Duplicate DLL name: {name}")
        names.add(name.lower())
        load = item.get("load")
        require(type(load) is bool, f"load must be boolean: {name}")
        has_entry |= load
        tokens = item.get("exclusive_claims", [])
        require(isinstance(tokens, list) and len(tokens) <= 16,
                f"Invalid claims array: {name}")
        require(load or not tokens, "A dependency cannot own claims")
        for token in tokens:
            require(isinstance(token, str) and re.fullmatch(r"[A-Za-z0-9._-]{1,128}", token),
                    f"Invalid claim: {token!r}")
            require(token not in claims, f"Duplicate claim in package: {token}")
            claims.add(token)
        require(nonblank(item.get("source")), f"Missing source: {name}")
        path = (base / item["source"]).resolve()
        size = pe_check(path)
        sources.append((path, size, {"name": name, "load": load,
                                     "exclusive_claims": tokens}))
    require(has_entry, "At least one DLL must have load=true")
    manifest = {"format": "ng4mod", "schema_version": 2,
                "mod_id": spec["mod_id"], "name": spec["name"], "version": spec["version"],
                "author": spec.get("author", ""), "description": spec.get("description", ""),
                "icon": "", "dependencies": dependencies,
                "game": {"id": "ninja-gaiden-4", "asset_database_sha256": ""},
                "assets": [], "files": [], "native_libraries": []}
    return manifest, sources


def build(spec_path, output):
    spec_path, output = Path(spec_path).resolve(), Path(output).resolve()
    require(not output.exists(), f"Refusing to overwrite: {output}")
    spec = json.loads(spec_path.read_text(encoding="utf-8-sig"))
    manifest, sources = prepare(spec, spec_path.parent)
    require(output.parent.is_dir(), "Create the output directory first")
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(prefix=".ng4mod-", suffix=".tmp",
                                         dir=output.parent, delete=False) as stream:
            temporary = Path(stream.name)
            prefix_hash = hashlib.sha256()
            for path, expected_size, item in sources:
                padding = bytes((-stream.tell()) % 16)
                stream.write(padding)
                prefix_hash.update(padding)
                item["payload_offset"] = stream.tell()
                digest = hashlib.sha256()
                copied = 0
                with path.open("rb") as source:
                    while chunk := source.read(1024 * 1024):
                        copied += len(chunk)
                        require(copied <= expected_size, f"DLL grew while reading: {path}")
                        stream.write(chunk)
                        prefix_hash.update(chunk)
                        digest.update(chunk)
                require(copied == expected_size, f"DLL changed size while reading: {path}")
                # The package describes the bytes actually read, not an earlier hash.
                item.update(payload_size=copied, payload_sha256=digest.hexdigest())
                manifest["native_libraries"].append(item)
            length = stream.tell()
            require(length <= (1 << 63) - 1, "Payload exceeds Int64")
            metadata = json.dumps(manifest, ensure_ascii=False, indent=2,
                                  allow_nan=False).encode("utf-8")
            require(1 <= len(metadata) <= 16 * 1024 * 1024, "Metadata exceeds v2 limit")
            trailer = bytearray(160)
            struct.pack_into("<8sII", trailer, 0, b"NG4MOD2\0", 160, 2)
            struct.pack_into("<qqq", trailer, 24, length, length, len(metadata))
            trailer[64:96] = prefix_hash.digest()
            trailer[96:128] = hashlib.sha256(metadata).digest()
            trailer[128:160] = hashlib.sha256(trailer).digest()
            stream.write(metadata)
            stream.write(trailer)
            stream.flush()
            os.fsync(stream.fileno())
        # Same-directory publication, atomic and no-overwrite, including race cases.
        os.link(temporary, output)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--spec", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        manifest = build(args.spec, args.output)
    except (OSError, ValueError) as error:
        parser.exit(1, f"Error: {error}\n")
    print(f"Created {args.output}: {len(manifest['native_libraries'])} DLL(s); "
          "full package validation and export/dependency checks still required.")


if __name__ == "__main__":
    main()
