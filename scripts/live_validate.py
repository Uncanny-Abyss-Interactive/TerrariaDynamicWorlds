"""Exercise a candidate package in disposable, real local tModLoader processes."""
from __future__ import annotations

from contextlib import contextmanager
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import tempfile
import time
import uuid


HARNESS = "DynamicWorldsValidation"
WORLD_NAME = "DWValidation"
INITIAL_SEED = "24681357"
REGEN_SEED = "8675309"
WORLD_TIMEOUT = 420
CLIENT_TIMEOUT = 600
SERVER_CHECKS = {"guard.isolated_world", "progress.capture_restore", "tile.snapshot_restore",
                 "chest.snapshot_restore", "structure.capture_restore", "fixture.ready"}
CLIENT_CHECKS = {"disposable_fixture_loaded", "gameplay_updates_120", "rendered_gameplay_frames_120",
                 "singleplayer", "player_alive", "saved_regeneration_seed_reloaded",
                 "saved_boss_progression_reloaded", "saved_combat_book_reloaded",
                 "saved_difficulty_reloaded", "saved_anchor_tile_reloaded", "saved_anchor_chest_reloaded",
                 "graphics_device_ready", "dynamic_worlds_loaded"}
CLIENT_CHECKS.update(name + suffix for name in ("RealityAnchor", "RealityEraser", "StructureAnchorItem", "BiomeDowser")
                     for suffix in ("_registered", "_in_inventory", "_texture_loaded"))
CLIENT_CHECKS.update({"initial_seed_matches", "initial_saved_fixtures_verified", "production_regeneration_requested",
                      "production_regeneration_busy_observed", "production_regeneration_idle", "world_unloaded_and_reloaded",
                      "regeneration_seed_changed", "saved_structure_and_chest_reloaded", "unprotected_sentinel_replaced"})
CLIENT_CHECKS.update({"microfix.guide_vanilla_buttons", "microfix.guide_dialogue_unchanged"})


class LiveValidationError(RuntimeError):
    pass


def read_report(path: Path, run_id: str) -> dict | None:
    if not path.exists():
        return None
    try:
        report = json.loads(path.read_text())
    except (OSError, json.JSONDecodeError):
        return None  # The writer publishes atomically; tolerate a transient read.
    if not isinstance(report, dict) or report.get("run_id") != run_id:
        raise LiveValidationError("Rejected a live-test report for a different run.")
    if report.get("phase") == "failed" or report.get("failures"):
        raise LiveValidationError(f"Live assertion failed: {report.get('error') or report.get('failures') or report}")
    return report


def validate_report(report: dict, required: set[str], phase: str):
    if report.get("schema_version") != 1 or report.get("phase") != phase or report.get("success") is not True:
        raise LiveValidationError("The live-test report did not confirm success.")
    checks = report.get("checks")
    if isinstance(checks, list):
        if any(not isinstance(entry, dict) or not isinstance(entry.get("name"), str)
               or entry.get("passed") is not True for entry in checks):
            raise LiveValidationError("The live-test report contains invalid or failed checks.")
        mapped = {entry["name"]: entry["passed"] for entry in checks}
        if len(mapped) != len(checks):
            raise LiveValidationError("The live-test report repeats an assertion name.")
        checks = mapped
    if not isinstance(checks, dict) or not required.issubset(checks) or any(value is not True for value in checks.values()):
        raise LiveValidationError("The live-test report contains missing or failed required checks.")


def wait_for(predicate, process, seconds: float, label: str):
    deadline = time.monotonic() + seconds
    while True:
        result = predicate()
        if result:
            return result
        if process.poll() is not None:
            raise LiveValidationError(f"tModLoader exited {process.returncode} before {label}; inspect the runtime logs.")
        if time.monotonic() >= deadline:
            raise LiveValidationError(f"Timed out after {seconds}s waiting for {label}.")
        time.sleep(0.2)


def wait_report(path, run_id, phase, process, seconds, console=None, runtime=None):
    def ready():
        if console is not None:
            assert_no_runtime_errors(console, runtime)
        report = read_report(path, run_id)
        return report if report and report.get("phase") == phase else None
    return wait_for(ready, process, seconds, phase)


def send(process, command):
    if process.poll() is not None:
        raise LiveValidationError("The test server exited before its next command.")
    try:
        process.stdin.write(command + "\n")
        process.stdin.flush()
    except (OSError, BrokenPipeError) as exc:
        raise LiveValidationError("Lost the test server's command channel.") from exc


def stop_owned_process(process):
    """Only signal the process group created by this test; never an existing game."""
    if process.poll() is not None:
        return
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        process.wait(timeout=10)
        return
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.wait(timeout=10)


@contextmanager
def running_game(args, runtime: Path, env: dict, log: Path):
    with log.open("w") as output:
        process = subprocess.Popen(args, cwd=runtime, env=env, stdin=subprocess.PIPE,
                                   stdout=output, stderr=subprocess.STDOUT, text=True,
                                   start_new_session=True)
        try:
            yield process
        finally:
            stop_owned_process(process)
            process.stdin.close()


def runtime_directory(root: Path, installation: Path, name: str) -> Path:
    runtime = root / name
    runtime.mkdir()
    # Use installed read-only assets, but keep logs/config discovery in this CWD.
    for name in ("Libraries", "Content"):
        (runtime / name).symlink_to(installation / name, target_is_directory=True)
    return runtime


def read_logs(console: Path, runtime: Path) -> str:
    paths = [console, *(path for path in sorted((runtime / "tModLoader-Logs").glob("*.log"))
                       if not path.name.startswith("environment-"))]
    return "\n".join(path.read_text(errors="replace") for path in paths if path.is_file())


def collect_logs(runtime: Path, evidence: Path):
    logs = runtime / "tModLoader-Logs"
    if logs.exists():
        shutil.copytree(logs, evidence / runtime.name, dirs_exist_ok=True,
                        ignore=shutil.ignore_patterns("environment-*.log"))


def assert_no_runtime_errors(console: Path, runtime: Path):
    bad = re.findall(r"^.*(?:/(?:ERROR|FATAL)\]|Unhandled exception|assertion failed).*$",
                     read_logs(console, runtime), re.MULTILINE | re.IGNORECASE)
    if bad:
        raise LiveValidationError("Runtime logged errors: " + " | ".join(bad[:5]))


def available_port() -> int:
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def validate_live(settings, commit: str, artifact: Path, run_dir: Path, deploy) -> dict:
    """Build the test-only mod, run real regeneration, then render the saved world."""
    deploy.ensure_game_stopped()
    evidence = run_dir / "live-validation"
    evidence.mkdir()
    run_id = uuid.uuid4().hex
    artifact_hash = deploy.sha256(artifact)
    summary = {"run_id": run_id, "git_sha": commit, "artifact_sha256": artifact_hash,
               "status": "running", "graphical_client": not settings.server_only}
    deploy.write_json(evidence / "summary.json", summary)
    try:
        with tempfile.TemporaryDirectory(prefix="dynamic-worlds-validation-") as temporary:
            root = Path(temporary).resolve()
            (root / ".dynamic-worlds-validation").write_text(run_id)
            mods = root / "Mods"
            mods.mkdir()
            shutil.copy2(artifact, mods / "DynamicWorlds.tmod")
            harness_source = deploy.archive_directory(settings.repo, commit, root / "source",
                                                       f"validation/{HARNESS}")
            harness = deploy.build_mod(settings, harness_source, root, evidence / "harness-build.log", HARNESS)
            harness_info = deploy.validate_tmod(harness, deploy.source_version(harness_source), HARNESS)
            enabled = ["DynamicWorlds", HARNESS]
            (mods / "enabled.json").write_text(json.dumps(enabled))
            packs = mods / "ModPacks"
            packs.mkdir(exist_ok=True)
            (packs / "validation.json").write_text(json.dumps(enabled))
            worlds = root / "Worlds"
            worlds.mkdir()
            empty_workshop = root / "empty-workshop"
            empty_workshop.mkdir()
            world = worlds / f"{WORLD_NAME}.wld"
            server_runtime = runtime_directory(root, settings.tml_dir, "server-runtime")
            env = deploy.runtime_environment()
            env.update(DOTNET_ROLL_FORWARD="LatestPatch", FNA_PLATFORM_BACKEND="NONE",
                       DYLD_LIBRARY_PATH=str(settings.tml_dir / "Libraries/Native/OSX"),
                       DW_VALIDATION_RUN_ID=run_id, DW_VALIDATION_ROOT=str(root),
                       DW_VALIDATION_MODE="server", DW_VALIDATION_RESULT=str(root / "server-result.json"),
                       DW_VALIDATION_REGEN_SEED=REGEN_SEED)
            executable = [str(deploy.dotnet_runtime(settings)), str(settings.tml_dir / "tModLoader.dll")]
            args = executable + ["-server", "-nosteam", "-noupnp", "-ip", "127.0.0.1",
                                 "-port", str(available_port()), "-players", "1", "-language", "en-US",
                                 "-tmlsavedirectory", str(root), "-modpack", "validation",
                                 "-steamworkshopfolder", str(empty_workshop),
                                 "-world", str(world), "-autocreate", "1", "-worldname", WORLD_NAME,
                                 "-seed", INITIAL_SEED]
            console = evidence / "server-console.log"
            print("Live validation: generating a disposable Terraria world...", flush=True)
            try:
                with running_game(args, server_runtime, env, console) as server:
                    wait_report(root / "server-result.json", run_id, "world_ready", server, WORLD_TIMEOUT, console, server_runtime)
                    send(server, "dwvalidate begin")
                    server_result = wait_report(root / "server-result.json", run_id, "fixtures_ready", server, 60, console, server_runtime)
                    validate_report(server_result, SERVER_CHECKS, "fixtures_ready")
                    shutil.copy2(root / "server-result.json", evidence / "server-result.json")
                    send(server, "exit")
                    try:
                        server.wait(timeout=45)
                    except subprocess.TimeoutExpired as exc:
                        raise LiveValidationError("Test server did not save and exit cleanly.") from exc
                    if server.returncode != 0:
                        raise LiveValidationError(f"Test server exited {server.returncode} after validation.")
                assert_no_runtime_errors(console, server_runtime)
                if not world.exists() or not world.with_suffix(".twld").exists():
                    raise LiveValidationError("The validated world was not saved with its mod data.")
                summary["server"] = server_result
            finally:
                collect_logs(server_runtime, evidence)
                if (root / "server-result.json").exists():
                    shutil.copy2(root / "server-result.json", evidence / "server-result.json")

            if not settings.server_only:
                # Fresh profiles otherwise wait at language/welcome/mod-change screens
                # before loading mods, where the in-game companion cannot run yet.
                (root / "config.json").write_text(json.dumps({
                    "Language": "en-US", "LastLaunchedTModLoaderVersion": harness_info["tmodloader_version"],
                    "LastLaunchedVersion": 279, "ShowNewUpdatedModsInfo": False,
                    "DisplayWidth": 800, "DisplayHeight": 720, "Fullscreen": False,
                }))
                client_runtime = runtime_directory(root, settings.tml_dir, "client-runtime")
                client_env = env.copy()
                client_env.pop("FNA_PLATFORM_BACKEND", None)
                client_env.update(DW_VALIDATION_MODE="client", DW_VALIDATION_RESULT=str(root / "client-result.json"))
                client_args = executable + ["-nosteam", "-tmlsavedirectory", str(root), "-modpack", "validation",
                                            "-steamworkshopfolder", str(empty_workshop),
                                            "-language", "en-US", "-skipselect", WORLD_NAME + ":" + WORLD_NAME]
                client_log = evidence / "client-console.log"
                print("Live validation: opening the graphical client in the disposable world...", flush=True)
                try:
                    with running_game(client_args, client_runtime, client_env, client_log) as client:
                        client_result = wait_report(root / "client-result.json", run_id, "passed", client, CLIENT_TIMEOUT, client_log, client_runtime)
                        validate_report(client_result, CLIENT_CHECKS, "passed")
                        try:
                            client.wait(timeout=30)
                        except subprocess.TimeoutExpired as exc:
                            raise LiveValidationError("Graphical test client did not close cleanly.") from exc
                        if client.returncode != 0:
                            raise LiveValidationError(f"Graphical test client exited {client.returncode}.")
                    assert_no_runtime_errors(client_log, client_runtime)
                    summary["client"] = client_result
                finally:
                    collect_logs(client_runtime, evidence)
                    for filename in ("client-result.json", "client.png"):
                        if (root / filename).exists():
                            shutil.copy2(root / filename, evidence / filename)
            if deploy.sha256(artifact) != artifact_hash or deploy.sha256(mods / "DynamicWorlds.tmod") != artifact_hash:
                raise LiveValidationError("The candidate mod changed during live validation.")
        summary["status"] = "passed"
        deploy.write_json(evidence / "summary.json", summary)
        return summary
    except Exception as exc:
        summary.update(status="failed", error=str(exc))
        deploy.write_json(evidence / "summary.json", summary)
        raise deploy.DeploymentError(f"Live validation failed: {exc}. See {evidence}") from exc
