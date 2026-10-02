# Git to local tModLoader deployment

Push a commit to `main` in
[Uncanny-Abyss-Interactive/TerrariaDynamicWorlds](https://github.com/Uncanny-Abyss-Interactive/TerrariaDynamicWorlds)
to run **Deploy Dynamic Worlds to tModLoader**. The workflow can also be rerun
manually on `main` from GitHub Actions.

The workflow uses a dedicated self-hosted runner on the development Mac with
the `dynamic-worlds-tmodloader` label. The Mac must be awake, logged in, and
connected to GitHub. Jobs wait while the runner is offline. Runner service
installation is a separate local setup step described below.

## What a deployment does

1. Runs the deployment and runner-guard tests on a GitHub-hosted Linux runner.
2. Checks out the pushed commit on the Mac and skips it if a newer `main`
   commit is queued.
3. Exports the selected Git commit to a temporary source folder. Uncommitted
   files in the developer checkout are never used or overwritten.
4. Compiles and packages with the installed tModLoader's internal compiler,
   using an isolated save directory. This Mac uses its native ARM64 .NET 8
   runtime to avoid Rosetta failures in tModLoader's bundled x64 runtime.
5. Verifies the resulting `DynamicWorlds.tmod`. A failed build leaves the
   installed mod untouched.
6. Checks that tModLoader is closed, backs up the installed package, then
   replaces it atomically and records its Git commit and SHA-256.
7. Keeps the package, build log, and receipt as GitHub Actions artifacts for
   14 days.

Close tModLoader before deploying. If a job refuses deployment because the game
is open, close it and rerun that workflow. Open tModLoader after the job succeeds
to load the new package. The pipeline installs a local mod; it does not publish
to Steam Workshop or modify worlds, players, enabled mods, or mod configuration.

## Local commands

Run these at the repository root using Python 3:

```sh
python3 -m unittest discover -s tests -v
python3 scripts/deploy.py --commit HEAD --dotnet /usr/local/share/dotnet/dotnet --build-only
python3 scripts/deploy.py --commit HEAD --dotnet /usr/local/share/dotnet/dotnet
```

`--build-only` produces an artifact without installing it. `--commit` also
accepts an earlier commit for a deliberate rollback. The script uses the
installed tModLoader version, so each receipt records the tModLoader DLL hash
as well as the source commit; rebuilding against a different tModLoader version
need not produce identical bytes.

Paths can be overridden with `--tml-dir`, `--saves-dir`, `--state-dir`, and
`--artifacts-dir`, or `DW_TML_DIR`, `DW_SAVES_DIR`, `DW_DEPLOY_STATE_DIR`, and
`DW_ARTIFACTS_DIR`. Use `--dotnet` or `DW_DOTNET` to select a compatible .NET 8
runtime; without an override the bundled runtime is used. Patch roll-forward
allows installed 8.0.x servicing updates without switching major versions.

Default macOS locations:

| Content | Location relative to your home directory |
| --- | --- |
| tModLoader installation | `Library/Application Support/Steam/steamapps/common/tModLoader` |
| Installed mod | `Library/Application Support/Terraria/tModLoader/Mods/DynamicWorlds.tmod` |
| Current deployment receipt | `Library/Application Support/DynamicWorldsDeploy/current.json` |
| Prior package backups | `Library/Application Support/DynamicWorldsDeploy/backups/` |
| Runner installation | `.local/share/dynamic-worlds-runner/` |

Backups here contain mod packages, not world saves. The original pre-pipeline
package is retained under `backups/pre-pipeline/`. To restore a package, close
tModLoader, copy the desired backup `DynamicWorlds.tmod` to the installed-mod
location, and restart tModLoader. Alternatively deploy its recorded Git commit.
A manual tModLoader build or manual package copy can supersede `current.json`;
compare the installed file's SHA-256 with the receipt when checking provenance.

## Source baseline

Commit `496e5cf` captures the previously uncommitted live 0.5.0 source. All 28
compiled C# documents matched the installed package's portable-PDB checksums.
The Workshop package contained the same code; its DLL differed only in build
timestamp and module GUID. Hashes and per-file evidence are recorded in
[docs/live-source-baseline.json](docs/live-source-baseline.json).

The existing `ModSources/DynamicWorlds` symlink points to this repository's
`DynamicWorlds` folder. Keep it for in-game development; deployment runs use a
separate runner checkout and never reset the developer checkout. Generated
`bin`, `obj`, and `.DS_Store` files are excluded from Git.

## Runner setup and maintenance

The runner executes this repository's `main` code as the logged-in macOS user.
Install and start it only after approving that persistent access. Download the
official macOS ARM64 runner from [GitHub's runner releases](https://github.com/actions/runner/releases)
and verify its release checksum. Register it to this repository using a temporary
registration token from GitHub's repository runner settings, with name
`dynamic-worlds-ryan-mac` and label `dynamic-worlds-tmodloader`.

Before starting the service, copy `scripts/runner-guard.sh` to
`~/Library/Application Support/DynamicWorldsDeploy/runner-guard.sh`, mark it
executable, and set `ACTIONS_RUNNER_HOOK_JOB_STARTED` in the runner's `.env` to
that absolute path. Keep the guard outside the runner directory and checkout.
It refuses other repositories, workflows, branches, and pull-request events
before job steps execute. Guard changes must be reviewed and copied locally.

From the configured runner directory:

```sh
./svc.sh install
./svc.sh start
./svc.sh status
```

This creates a user LaunchAgent. Keep the runner's automatic updates enabled.
To pause automatic deployments, run `./svc.sh stop`; restart with `./svc.sh start`.
To remove the background service, use `./svc.sh stop` and `./svc.sh uninstall`,
then remove the runner registration in GitHub Settings → Actions → Runners.

The workflow has read-only repository permissions, does not retain checkout
credentials, and serializes deployments without cancelling an installation in
progress. Do not add pull-request triggers to this local deployment workflow.
