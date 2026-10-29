"""Calculate plugin versions and generate Jellyfin repository metadata."""

import argparse
import copy
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "Jellyfin.Plugin.ThePornDB" / "ThePornDB.csproj"


def build_version(revision, project=PROJECT):
    prefix = ET.parse(project).findtext("./PropertyGroup/VersionPrefix")
    if not prefix or not re.fullmatch(r"\d+\.\d+\.\d+", prefix):
        raise ValueError("The project must define a three-part VersionPrefix.")
    if not 1 <= revision <= 65534:
        raise ValueError("Build revision must be between 1 and 65534.")
    version = f"{prefix}.{revision}"
    validate_version(version)
    return version


def validate_version(version):
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version):
        raise ValueError("A four-part numeric version is required.")
    if any(int(part) > 65534 for part in version.split(".")):
        raise ValueError("Version components must not exceed 65534.")


def create_manifest(template, version, repository, archive, timestamp=None):
    validate_version(version)
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*", repository):
        raise ValueError("Expected repository in owner/name format.")
    data = copy.deepcopy(template)
    if len(data) != 1 or data[0]["guid"] != "fb7580cf-576d-4991-8e56-0b4520c111d3":
        raise ValueError("Expected the ThePornDB plugin manifest.")
    # Jellyfin requires the MD5 checksum of the exact downloadable ZIP.
    checksum = hashlib.md5(Path(archive).read_bytes(), usedforsecurity=False).hexdigest()
    controller = ET.parse(PROJECT).find(".//PackageReference[@Include='Jellyfin.Controller']")
    abi = controller.attrib["Version"].split(".")
    if len(abi) != 3 or not all(part.isdigit() for part in abi):
        raise ValueError("Jellyfin.Controller must have a stable three-part version.")
    entry = {
        "version": version,
        "changelog": f"Automated build {version}. See the GitHub release for changes.",
        "targetAbi": ".".join(abi + ["0"]),
        "sourceUrl": f"https://github.com/{repository}/releases/download/v{version}/Jellyfin.Plugin.ThePornDB.zip",
        "checksum": checksum,
        "timestamp": timestamp or datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    data[0]["owner"] = repository.split("/")[0]
    data[0]["versions"] = [entry] + [
        item for item in data[0]["versions"] if item["version"] != version
    ]
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    version = commands.add_parser("version")
    version.add_argument("--revision", type=int, required=True)
    manifest = commands.add_parser("manifest")
    manifest.add_argument("--version", required=True)
    manifest.add_argument("--repository", required=True)
    manifest.add_argument("--archive", type=Path, required=True)
    manifest.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "version":
        print(build_version(args.revision))
    else:
        template = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
        data = create_manifest(template, args.version, args.repository, args.archive)
        args.output.write_text(json.dumps(data, indent=4) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
