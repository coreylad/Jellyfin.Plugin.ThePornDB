import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

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
            notes = "## Changes\n- Improved filename matching.\n- Updated dependencies."
            data = create_manifest(
                template, "1.6.1.42", "coreylad/Jellyfin.Plugin.ThePornDB",
                archive, "2026-10-29T00:00:00Z", changelog=f"\n{notes}\n",
            )
            latest = data[0]["versions"][0]
            self.assertEqual("1.6.1.42", latest["version"])
            controller_version = ET.parse(ROOT / "Jellyfin.Plugin.ThePornDB" / "ThePornDB.csproj").find(
                ".//PackageReference[@Include='Jellyfin.Controller']"
            ).attrib["Version"]
            self.assertEqual(f"{controller_version}.0", latest["targetAbi"])
            self.assertEqual(hashlib.md5(archive.read_bytes()).hexdigest(), latest["checksum"])
            self.assertEqual("https://github.com/coreylad/Jellyfin.Plugin.ThePornDB/releases/download/v1.6.1.42/Jellyfin.Plugin.ThePornDB.zip", latest["sourceUrl"])
            self.assertEqual("2026-10-29T00:00:00Z", latest["timestamp"])
            self.assertEqual(template[0]["versions"], data[0]["versions"][1:])
            self.assertEqual("coreylad", data[0]["owner"])
            self.assertIn(notes, latest["changelog"])
            self.assertIn("ThePornDB 1.6.1.42", latest["changelog"])
            self.assertIn("https://github.com/coreylad/Jellyfin.Plugin.ThePornDB/releases/tag/v1.6.1.42", latest["changelog"])
            self.assertIn(f"Requires Jellyfin {controller_version} or newer", latest["changelog"])
            self.assertIn("Restart Jellyfin after updating.", latest["changelog"])
            for field in ("description", "overview", "imageUrl"):
                self.assertEqual(template[0][field], data[0][field])
            repeated = create_manifest(data, "1.6.1.42", "coreylad/Jellyfin.Plugin.ThePornDB", archive, changelog=notes)
            self.assertEqual(len(data[0]["versions"]), len(repeated[0]["versions"]))

    def test_invalid_repository_and_missing_archive_fail(self):
        template = json.loads((ROOT / "manifest.json").read_text())
        with self.assertRaises(ValueError):
            create_manifest(template, "1.6.1.42", "../invalid", Path("missing.zip"), changelog="Changes")
        with self.assertRaises(FileNotFoundError):
            create_manifest(template, "1.6.1.42", "owner/repo", Path("missing.zip"), changelog="Changes")

    def test_empty_release_notes_fail(self):
        template = json.loads((ROOT / "manifest.json").read_text())
        for notes in ("", " \n\t"):
            with self.subTest(notes=notes), self.assertRaisesRegex(ValueError, "Release notes must not be empty"):
                create_manifest(template, "1.6.1.42", "owner/repo", Path("missing.zip"), changelog=notes)

    def test_manifest_command_reads_release_notes(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            archive = directory / "plugin.zip"
            archive.write_bytes(b"test archive")
            notes = directory / "notes.md"
            notes.write_text("## Changes\n- Improved metadata.", encoding="utf-8")
            output = directory / "manifest.json"
            subprocess.run(
                [
                    sys.executable, str(ROOT / "scripts" / "release.py"), "manifest",
                    "--version", "1.6.1.42",
                    "--repository", "coreylad/Jellyfin.Plugin.ThePornDB",
                    "--archive", str(archive),
                    "--changelog-file", str(notes),
                    "--output", str(output),
                ],
                check=True, capture_output=True, text=True,
            )
            data = json.loads(output.read_text(encoding="utf-8"))
            self.assertIn(notes.read_text(encoding="utf-8"), data[0]["versions"][0]["changelog"])
            self.assertEqual("1.6.1.42", data[0]["versions"][0]["version"])


if __name__ == "__main__":
    unittest.main()
