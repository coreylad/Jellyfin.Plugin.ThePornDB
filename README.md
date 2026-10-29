# Jellyfin.Plugin.ThePornDB

This metadata provider helps fill Jellyfin/Emby with information for your adult videos by pulling from [ThePornDB](https://theporndb.net)

[![GPL 2.0 License](https://img.shields.io/github/license/coreylad/Jellyfin.Plugin.ThePornDB)](./LICENSE)
[![Current Release](https://img.shields.io/github/release/coreylad/Jellyfin.Plugin.ThePornDB)](https://github.com/coreylad/Jellyfin.Plugin.ThePornDB/releases/latest)
[![Build status](https://img.shields.io/github/actions/workflow/status/coreylad/Jellyfin.Plugin.ThePornDB/build.yml)](https://github.com/coreylad/Jellyfin.Plugin.ThePornDB/actions/workflows/build.yml)

------------

## Install
- Repository (Jellyfin only):
  - Add to the list this URL `https://github.com/coreylad/Jellyfin.Plugin.ThePornDB/releases/latest/download/manifest.json`
- Manual:
  - Download Archive from [Latest Release](https://github.com/coreylad/Jellyfin.Plugin.ThePornDB/releases/latest)
  - Follow the [Instruction](https://jellyfin.org/docs/general/server/plugins/index.html)

## Builds and automatic revisions

Pull requests to `main`, pushes to `main`, and manual **Build** workflow runs
compile Debug and Release configurations for both Jellyfin and Emby using .NET 10.
Checkouts and release publishing use the triggering repository
(`coreylad/Jellyfin.Plugin.ThePornDB`), not the upstream repository.
Plugin regression tests and release-metadata tests must pass before publishing.
Release ZIPs and test results are downloadable from each workflow run.

Versions use `<VersionPrefix>.<workflow run number>` (for example `1.6.1.42`),
with the prefix defined in `ThePornDB.csproj`. Assembly and file versions use the
same value. Local builds default to revision `0`; override it with
`dotnet build Jellyfin.Plugin.ThePornDB/ThePornDB.csproj -p:RevisionNumber=42`.
Workflow reruns retain the original revision. The run number must be at most
65534, the .NET assembly-version limit; the version step fails explicitly beyond
that limit. No version-bump commits or personal access token are needed.

After all checks pass on `main`, the release workflow creates a versioned tag and
GitHub release with Jellyfin/Emby ZIPs and a generated Jellyfin repository manifest.
The manifest includes the ZIP checksum, matching version, current Jellyfin ABI,
and release download URL. Releases require the workflow's `contents: write`
permission. PRs only build and test; they never publish.

Use the **release-hosted manifest URL above** for automatic Jellyfin updates.
The checked-in `manifest.json` is historical input, not the live update feed.
The new feed becomes available after the first successful release on `main`.
Historical upstream entries remain unchanged because their URLs and checksums
refer to the original upstream binaries.

## Enhanced matching and automatic filenames

Enable **Use enhanced filename matching** in the plugin settings to use matching
inspired by [ThePornDatabase/namer](https://github.com/ThePornDatabase/namer).
It searches the original name and cleaned filename, removes common encoding and
resolution tokens, and deduplicates results by TPDB ID. Generic filenames such as
`video.mp4` use their parent folder's name instead.

Names such as `ExampleStudio.24.01.15.Scene.Title.Performer.Name.1080p.mp4` are
compared with the studio, release date, title and performers. Two-digit years mean
20xx; release dates allow a one-day timezone difference. Conflicting studios or
dates prevent automatic matching at the default threshold. Automatic identification
requires a score of at least **85** and a **10-point lead** over the next result.
Uncertain results remain available through Jellyfin's **Identify** dialog.
Explicit TPDB IDs and manual selections still take precedence. Existing hash lookup
is retained, but is not treated as proof of a match without filename agreement.
Enhanced matching overrides Order Style; turn it off to retain legacy matching.
Both enhanced matching and renaming are disabled by default.

Enable **Automatically rename confidently matched video files** to change actual
files **in place** after Jellyfin library scans or during the hourly **Rename matched
videos** task. Emby uses the scheduled task only. The task only processes movies
already identified by this plugin and independently checks that a confident
filename match agrees with the stored ID. Back up your library before enabling it.

The default filename format is `{studio} - {release_date} - {title}`. Supported
placeholders are `{studio}`, `{release_date}`, `{title}` and `{actors}`. For example:

```text
Example Studio - 2024-01-15 - Scene Title [TPDBID=scene-uuid].mp4
```

The original extension is retained, unsafe filename characters are removed, names
are length-limited, and the TPDB ID is always included for future identification.
Files containing a TPDBID marker are left alone on subsequent runs. Collisions
never overwrite files. The library path is updated while retaining the item's ID;
if that update fails, the file move is rolled back and the error is surfaced.

This first in-plugin renamer only supports standalone local video files. Files
with matching-basename sidecars (such as NFOs, subtitles or artwork), stacked
videos, alternate versions and symbolic links are skipped with a log message.
Recently modified files are deferred for at least two minutes.
Folders are not moved or renamed. Use Namer for those workflows, perceptual-hash
matching, embedded MP4 tags, or an ingestion/watch-folder service.
