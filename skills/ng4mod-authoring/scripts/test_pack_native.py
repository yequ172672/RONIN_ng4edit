"""Container/negative tests; synthetic PE headers here are never executable DLLs."""
import copy
import hashlib
import json
from pathlib import Path
import struct
import tempfile
import unittest

from pack_native import build, prepare


class NativePackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        pe = bytearray(265)
        pe[:2] = b"MZ"
        struct.pack_into("<I", pe, 60, 64)
        pe[64:68] = b"PE\0\0"
        struct.pack_into("<H", pe, 68, 0x8664)
        struct.pack_into("<HHH", pe, 84, 112, 0x2000, 0x20B)
        self.pe = bytes(pe)
        (self.root / "input.dll").write_bytes(pe)
        self.spec = {"mod_id": "test.example", "name": "测试", "version": "1.0",
                     "native_libraries": [
                         {"source": "input.dll", "name": "entry.dll", "load": True,
                          "exclusive_claims": ["test.owner.v1"]},
                         {"source": "input.dll", "name": "helper.dll", "load": False}]}

    def write_package(self):
        spec = self.root / "spec.json"
        spec.write_text(json.dumps(self.spec, ensure_ascii=False), encoding="utf-8")
        output = self.root / "result.ng4mod"
        build(spec, output)
        return spec, output

    def test_envelope_hashes_alignment_and_payloads(self):
        _, output = self.write_package()
        data = output.read_bytes()
        trailer = bytearray(data[-160:])
        self.assertEqual(struct.unpack_from("<8sII", trailer), (b"NG4MOD2\0", 160, 2))
        length, offset, count = struct.unpack_from("<qqq", trailer, 24)
        self.assertEqual(length, offset)
        self.assertEqual(offset + count + 160, len(data))
        self.assertEqual(trailer[16:24] + trailer[48:64], bytes(24))
        self.assertEqual(trailer[64:96], hashlib.sha256(data[:length]).digest())
        metadata = data[offset:offset + count]
        self.assertEqual(trailer[96:128], hashlib.sha256(metadata).digest())
        checksum = trailer[128:160]
        trailer[128:160] = bytes(32)
        self.assertEqual(checksum, hashlib.sha256(trailer).digest())
        manifest = json.loads(metadata)
        self.assertEqual(manifest["assets"], [])
        self.assertEqual(manifest["files"], [])
        previous = 0
        for item in manifest["native_libraries"]:
            start, size = item["payload_offset"], item["payload_size"]
            self.assertEqual(start % 16, 0)
            self.assertEqual(data[previous:start], bytes(start - previous))
            self.assertEqual(data[start:start + size], self.pe)
            self.assertEqual(item["payload_sha256"], hashlib.sha256(self.pe).hexdigest())
            previous = start + size
        self.assertEqual(previous, length)

    def test_rejects_invalid_contracts(self):
        mutations = [
            lambda s: s.update(assets=[]),
            lambda s: s.update(dependencies=None),
            lambda s: s["native_libraries"][0].update(load=False),
            lambda s: s["native_libraries"][0].update(name="../bad.dll"),
            lambda s: s["native_libraries"][1].update(name="ENTRY.DLL"),
            lambda s: s["native_libraries"][1].update(exclusive_claims=["bad"]),
            lambda s: s["native_libraries"][1].update(
                load=True, exclusive_claims=["test.owner.v1"]),
            lambda s: s["native_libraries"][0].update(exclusive_claims=["bad/token"]),
            lambda s: s["native_libraries"][0].update(load="true"),
        ]
        for change in mutations:
            spec = copy.deepcopy(self.spec)
            change(spec)
            with self.subTest(spec=spec), self.assertRaises(ValueError):
                prepare(spec, self.root)

    def test_rejects_non_x64_without_publication(self):
        pe = bytearray(self.pe)
        struct.pack_into("<H", pe, 68, 0x14C)
        (self.root / "input.dll").write_bytes(pe)
        with self.assertRaisesRegex(ValueError, "x64"):
            self.write_package()
        self.assertFalse((self.root / "result.ng4mod").exists())

    def test_does_not_overwrite(self):
        spec, output = self.write_package()
        original = output.read_bytes()
        with self.assertRaisesRegex(ValueError, "overwrite"):
            build(spec, output)
        self.assertEqual(original, output.read_bytes())


if __name__ == "__main__":
    unittest.main()
