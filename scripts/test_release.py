import hashlib
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

from release import ROOT, build_version, create_manifest, validate_version


class ReleaseTests(unittest.TestCase):
    def test_revision_and_prefix(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / "Test.csproj"
            project.write_text("<Project><PropertyGroup><VersionPrefix>2.3.4</VersionPrefix></PropertyGroup></Project>")
            self.assertEqual("2.3.4.42", build_version(42, project))
            self.assertEqual("2.3.4.65534", build_version(65534, project))

    def test_invalid_revisions_fail(self):
        for revision in (0, -1, 65535):
            with self.subTest(revision=revision), self.assertRaises(ValueError):
                build_version(revision)

    def test_invalid_versions_fail(self):
        for version in ("v1.2.3.4", "1.2.3", "1.2.3.4.5", "1.2.3.65535", "1.2.x.4"):
            with self.subTest(version=version), self.assertRaises(ValueError):
                validate_version(version)

    def test_manifest_matches_archive_and_retains_history(self):
        template = json.loads((ROOT / "manifest.json").read_text())
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "Jellyfin.Plugin.ThePornDB.zip"
            with zipfile.ZipFile(archive, "w") as package:
                package.writestr("Jellyfin.Plugin.ThePornDB.dll", b"test assembly")
            data = create_manifest(template, "1.6.1.42", "coreylad/Jellyfin.Plugin.ThePornDB", archive, "2026-10-29T00:00:00Z")
            latest = data[0]["versions"][0]
            self.assertEqual("1.6.1.42", latest["version"])
            self.assertEqual("12.0.0.0", latest["targetAbi"])
            self.assertEqual(hashlib.md5(archive.read_bytes()).hexdigest(), latest["checksum"])
            self.assertEqual("https://github.com/coreylad/Jellyfin.Plugin.ThePornDB/releases/download/v1.6.1.42/Jellyfin.Plugin.ThePornDB.zip", latest["sourceUrl"])
            self.assertEqual("2026-10-29T00:00:00Z", latest["timestamp"])
            self.assertEqual(template[0]["versions"], data[0]["versions"][1:])
            self.assertEqual("coreylad", data[0]["owner"])
            repeated = create_manifest(data, "1.6.1.42", "coreylad/Jellyfin.Plugin.ThePornDB", archive)
            self.assertEqual(len(data[0]["versions"]), len(repeated[0]["versions"]))

    def test_invalid_repository_and_missing_archive_fail(self):
        template = json.loads((ROOT / "manifest.json").read_text())
        with self.assertRaises(ValueError):
            create_manifest(template, "1.6.1.42", "../invalid", Path("missing.zip"))
        with self.assertRaises(FileNotFoundError):
            create_manifest(template, "1.6.1.42", "owner/repo", Path("missing.zip"))


if __name__ == "__main__":
    unittest.main()
