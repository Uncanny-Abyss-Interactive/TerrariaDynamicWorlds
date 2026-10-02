#!/usr/bin/env python3
"""Build a committed Dynamic Worlds revision and atomically install its .tmod."""

from __future__ import annotations

import argparse
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import datetime, timezone
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import struct
import subprocess
import sys
import tarfile
import tempfile


MOD_NAME = "DynamicWorlds"
BUILD_TIMEOUT_SECONDS = 600


class DeploymentError(RuntimeError):
    pass


@dataclass
class Settings:
    repo: Path
    commit: str
    tml_dir: Path
    saves_dir: Path
    state_dir: Path
    artifacts_dir: Path
    build_only: bool = False
    dotnet: Path | None = None


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def command_output(args: list[str], cwd: Path | None = None) -> str:
    try:
        result = subprocess.run(args, cwd=cwd, capture_output=True, text=True,
                                timeout=30, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise DeploymentError(f"Could not run {args[0]}: {exc}") from exc
    if result.returncode:
        raise DeploymentError(result.stderr.strip() or f"{args[0]} exited {result.returncode}")
    return result.stdout.strip()


def resolve_commit(repo: Path, revision: str) -> str:
    return command_output(["git", "rev-parse", "--verify", "--end-of-options",
                           f"{revision}^{{commit}}"], cwd=repo)


def archive_source(repo: Path, commit: str, destination: Path) -> Path:
    """Use Git objects, never a copy of the potentially dirty working tree."""
    archive = destination / "source.tar"
    command_output(["git", "archive", "--format=tar", f"--output={archive}",
                    commit, MOD_NAME], cwd=repo)
    with tarfile.open(archive) as source:
        for member in source:
            name = PurePosixPath(member.name)
            if name.is_absolute() or ".." in name.parts or not name.parts or name.parts[0] != MOD_NAME:
                raise DeploymentError(f"Unsafe Git archive path: {member.name}")
            target = destination.joinpath(*name.parts)
            if member.isdir():
                target.mkdir(parents=True, exist_ok=True)
            elif member.isfile():
                target.parent.mkdir(parents=True, exist_ok=True)
                with source.extractfile(member) as incoming, target.open("wb") as outgoing:
                    shutil.copyfileobj(incoming, outgoing)
            else:
                raise DeploymentError(f"Source archive contains an unsupported link or special file: {member.name}")
    source_dir = destination / MOD_NAME
    if not (source_dir / "build.txt").is_file() or not any(source_dir.rglob("*.cs")):
        raise DeploymentError(f"Commit {commit} does not contain a complete {MOD_NAME} source tree.")
    return source_dir


def source_version(source: Path) -> str:
    for line in (source / "build.txt").read_text(encoding="utf-8-sig").splitlines():
        key, separator, value = line.partition("=")
        if separator and key.strip() == "version" and value.strip():
            return value.strip()
    raise DeploymentError("Committed build.txt has no version.")


def validate_tmod(path: Path, expected_version: str) -> dict[str, str]:
    """Check the tML package header, payload hash, identity, and assembly entry."""
    data = path.read_bytes()
    stream = io.BytesIO(data)

    def read_exact(count: int) -> bytes:
        value = stream.read(count)
        if len(value) != count:
            raise DeploymentError(f"Truncated mod package: {path}")
        return value

    def read_string() -> str:
        length = 0
        for shift in range(0, 35, 7):
            value = read_exact(1)[0]
            length |= (value & 127) << shift
            if value < 128:
                if length > len(data):
                    break
                try:
                    return read_exact(length).decode("utf-8")
                except UnicodeDecodeError as exc:
                    raise DeploymentError(f"Invalid package text: {path}") from exc
        raise DeploymentError(f"Invalid package string length: {path}")

    def read_int() -> int:
        return struct.unpack("<i", read_exact(4))[0]

    if len(data) < 290 or read_exact(4) != b"TMOD":
        raise DeploymentError(f"Not a valid nonempty TMOD package: {path}")
    tml_version = read_string()
    expected_hash = read_exact(20)
    read_exact(256)  # Optional publishing signature.
    payload_length = read_int()
    payload = data[stream.tell():]
    if payload_length != len(payload) or hashlib.sha1(payload).digest() != expected_hash:
        raise DeploymentError(f"Package length/hash verification failed: {path}")
    name, version = read_string(), read_string()
    if name != MOD_NAME or version != expected_version:
        raise DeploymentError(f"Expected {MOD_NAME} {expected_version}, found {name} {version}.")
    count = read_int()
    if count < 1 or count > len(payload) // 9:
        raise DeploymentError(f"Invalid package file count: {path}")
    stored_bytes = 0
    has_assembly = False
    for _ in range(count):
        filename, unpacked, stored = read_string(), read_int(), read_int()
        if unpacked < 0 or stored < 0:
            raise DeploymentError(f"Invalid package entry size: {path}")
        stored_bytes += stored
        has_assembly |= filename == f"{MOD_NAME}.dll" and unpacked > 0 and stored > 0
    if not has_assembly or stream.tell() + stored_bytes != len(data):
        raise DeploymentError(f"Package assembly or file table is incomplete: {path}")
    return {"mod_version": version, "tmodloader_version": tml_version}


def dotnet_runtime(settings: Settings) -> Path:
    return (settings.dotnet or settings.tml_dir / "dotnet" / "dotnet").resolve()


def build_mod(settings: Settings, source: Path, stage: Path, log: Path) -> Path:
    runtime = dotnet_runtime(settings)
    loader = settings.tml_dir / "tModLoader.dll"
    if not runtime.is_file() or not os.access(runtime, os.X_OK) or not loader.is_file():
        raise DeploymentError(f"tModLoader or the executable runtime {runtime} was not found. Set --tml-dir and, if needed, --dotnet.")
    environment = os.environ.copy()
    # Allow servicing updates within the required .NET major/minor version.
    environment["DOTNET_ROLL_FORWARD"] = "LatestPatch"
    # -build exits before tML's dedicated-server graphics initialization.
    # Select the headless backend before PNG conversion starts worker threads.
    environment["FNA_PLATFORM_BACKEND"] = "NONE"
    environment["DYLD_LIBRARY_PATH"] = str(settings.tml_dir / "Libraries" / "Native" / "OSX")
    args = [str(runtime), "tModLoader.dll", "-server", "-build", str(source),
            "-tmlsavedirectory", str(stage)]
    with log.open("w", encoding="utf-8") as output:
        try:
            result = subprocess.run(args, cwd=settings.tml_dir, env=environment,
                                    stdout=output, stderr=subprocess.STDOUT,
                                    timeout=BUILD_TIMEOUT_SECONDS, check=False)
        except (OSError, subprocess.TimeoutExpired) as exc:
            raise DeploymentError(f"Build did not complete: {exc}. See {log}") from exc
    if result.returncode:
        raise DeploymentError(f"tModLoader build failed (exit {result.returncode}). See {log}")
    artifact = stage / "Mods" / f"{MOD_NAME}.tmod"
    if not artifact.is_file():
        raise DeploymentError(f"Build returned success without {artifact}. See {log}")
    return artifact


def active_tml_processes() -> list[str]:
    """Match executable names first so shell/script text cannot look like a game."""
    listing = command_output(["ps", "-axo", "pid=,comm="])
    candidates: dict[str, str] = {}
    names = {"dotnet", "tModLoader", "tModLoader.exe", "tModLoaderServer", "tModLoaderServer.exe"}
    for line in listing.splitlines():
        columns = line.strip().split(None, 1)
        if len(columns) == 2 and Path(columns[1]).name in names and int(columns[0]) != os.getpid():
            candidates[columns[0]] = Path(columns[1]).name
    if not candidates:
        return []
    # Processes may exit between the two snapshots; ps returns 1 if none remain.
    result = subprocess.run(["ps", "-p", ",".join(candidates), "-o", "pid=,args="],
                            capture_output=True, text=True, timeout=30, check=False)
    if result.returncode not in (0, 1):
        raise DeploymentError(f"Could not inspect running tModLoader processes: {result.stderr.strip()}")
    active = []
    for line in result.stdout.splitlines():
        columns = line.strip().split(None, 1)
        if len(columns) != 2 or columns[0] not in candidates:
            continue
        pid, arguments = columns
        if re.search(r"(?:^|\s)-build(?:\s|$)", arguments):
            continue
        if candidates[pid] != "dotnet" or re.search(r"(?:^|[/\\\s])tModLoader\.dll(?:\s|$)", arguments):
            active.append(f"PID {pid}: {arguments}")
    return active


def ensure_game_stopped() -> None:
    active = active_tml_processes()
    if active:
        raise DeploymentError("Quit tModLoader and stop its dedicated server before deploying, then rerun the command. "
                              "The existing mod is unchanged.\n" + "\n".join(active))


@contextmanager
def deployment_lock(state_dir: Path):
    state_dir.mkdir(parents=True, exist_ok=True)
    with (state_dir / "deploy.lock").open("a+") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as exc:
            raise DeploymentError("Another Dynamic Worlds build/deploy is running. Wait for it to finish and retry.") from exc
        try:
            yield
        finally:
            fcntl.flock(lock, fcntl.LOCK_UN)


def prepared_copy(source: Path, destination_dir: Path) -> Path:
    fd, filename = tempfile.mkstemp(prefix=".dynamic-worlds-", dir=destination_dir)
    path = Path(filename)
    try:
        with os.fdopen(fd, "wb") as output, source.open("rb") as incoming:
            shutil.copyfileobj(incoming, output)
            output.flush()
            os.fsync(output.fileno())
        os.chmod(path, 0o644)
        return path
    except BaseException:
        path.unlink(missing_ok=True)
        raise


def write_json(path: Path, value: dict) -> None:
    with path.open("w", encoding="utf-8") as output:
        json.dump(value, output, indent=2, sort_keys=True)
        output.write("\n")
        output.flush()
        os.fsync(output.fileno())


def deploy_artifact(settings: Settings, artifact: Path, receipt: dict) -> dict:
    ensure_game_stopped()
    mods = settings.saves_dir / "Mods"
    mods.mkdir(parents=True, exist_ok=True)
    settings.state_dir.mkdir(parents=True, exist_ok=True)
    live = mods / f"{MOD_NAME}.tmod"
    current_receipt = settings.state_dir / "current.json"
    # Avoid replacing a linked file whose target is outside this deployment.
    if live.is_symlink() or current_receipt.is_symlink():
        raise DeploymentError("The live mod or deployment receipt is a symlink; use a regular deployment file.")
    backup = None
    if live.exists() or current_receipt.exists():
        backup_root = settings.state_dir / "backups"
        backup_root.mkdir(parents=True, exist_ok=True)
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ")
        backup = Path(tempfile.mkdtemp(prefix=stamp + "-", dir=backup_root))
        if live.exists():
            shutil.copy2(live, backup / live.name)
            if sha256(live) != sha256(backup / live.name):
                raise DeploymentError("Previous mod backup verification failed; deployment cancelled.")
        if current_receipt.exists():
            shutil.copy2(current_receipt, backup / "current.json")
    final_receipt = dict(receipt, deployed_at=datetime.now(timezone.utc).isoformat(),
                         deployed_path=str(live), backup_dir=str(backup) if backup else None)
    replacement = prepared_copy(artifact, mods)
    receipt_fd, receipt_name = tempfile.mkstemp(prefix=".receipt-", dir=settings.state_dir)
    os.close(receipt_fd)
    receipt_temp = Path(receipt_name)
    installed = False
    try:
        write_json(receipt_temp, final_receipt)
        ensure_game_stopped()
        os.replace(replacement, live)
        installed = True
        if sha256(live) != receipt["artifact_sha256"]:
            raise DeploymentError("Installed mod hash does not match the built artifact; restoring the previous mod.")
        os.replace(receipt_temp, current_receipt)
    except BaseException:
        if installed:
            if backup and (backup / live.name).exists():
                rollback = prepared_copy(backup / live.name, mods)
                os.replace(rollback, live)
            else:
                live.unlink(missing_ok=True)
        raise
    finally:
        replacement.unlink(missing_ok=True)
        receipt_temp.unlink(missing_ok=True)
    return final_receipt


def run(settings: Settings) -> dict:
    with deployment_lock(settings.state_dir):
        commit = resolve_commit(settings.repo, settings.commit)
        settings.artifacts_dir.mkdir(parents=True, exist_ok=True)
        run_dir = Path(tempfile.mkdtemp(prefix=commit[:12] + "-", dir=settings.artifacts_dir))
        log = run_dir / "build.log"
        print(f"Building Git commit {commit}\nBuild log: {log}", flush=True)
        with tempfile.TemporaryDirectory(prefix="dynamic-worlds-build-") as temporary:
            temporary_dir = Path(temporary)
            source = archive_source(settings.repo, commit, temporary_dir)
            version = source_version(source)
            loader_hash = sha256(settings.tml_dir / "tModLoader.dll")
            runtime = dotnet_runtime(settings)
            runtime_hash = sha256(runtime)
            built = build_mod(settings, source, temporary_dir / "tml-stage", log)
            package = validate_tmod(built, version)
            if sha256(settings.tml_dir / "tModLoader.dll") != loader_hash:
                raise DeploymentError("tModLoader changed during the build; retry after its update finishes.")
            if sha256(runtime) != runtime_hash:
                raise DeploymentError("The dotnet executable changed during the build; retry after its update finishes.")
            artifact = run_dir / f"{MOD_NAME}.tmod"
            shutil.copy2(built, artifact)
        receipt = dict(package, git_sha=commit, artifact_sha256=sha256(artifact),
                       tmodloader_dll_sha256=loader_hash, artifact_path=str(artifact),
                       dotnet_path=str(runtime), dotnet_sha256=runtime_hash,
                       dotnet_roll_forward="LatestPatch",
                       built_at=datetime.now(timezone.utc).isoformat())
        write_json(run_dir / "build.json", receipt)
        if not settings.build_only:
            receipt = deploy_artifact(settings, artifact, receipt)
            write_json(run_dir / "deployment.json", receipt)
            print(f"Deployed {MOD_NAME} {version}: {receipt['deployed_path']}")
        else:
            print(f"Build verified; live mod unchanged. Artifact: {artifact}")
        print(f"Artifact SHA-256: {receipt['artifact_sha256']}")
        return receipt


def main(argv: list[str] | None = None) -> int:
    home = Path.home()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--commit", default="HEAD", help="Git revision to archive and build (default: HEAD)")
    parser.add_argument("--build-only", action="store_true", help="Verify a package without installing it")
    parser.add_argument("--tml-dir", type=Path, default=Path(os.environ.get("DW_TML_DIR", home / "Library/Application Support/Steam/steamapps/common/tModLoader")))
    parser.add_argument("--dotnet", type=Path, default=os.environ.get("DW_DOTNET"),
                        help="dotnet executable (default: DW_DOTNET or tModLoader's bundled runtime)")
    parser.add_argument("--saves-dir", type=Path, default=Path(os.environ.get("DW_SAVES_DIR", home / "Library/Application Support/Terraria/tModLoader")))
    parser.add_argument("--state-dir", type=Path, default=Path(os.environ.get("DW_DEPLOY_STATE_DIR", home / "Library/Application Support/DynamicWorldsDeploy")))
    parser.add_argument("--artifacts-dir", type=Path, default=Path(os.environ.get("DW_ARTIFACTS_DIR", "artifacts")))
    args = parser.parse_args(argv)
    repo = Path(__file__).resolve().parents[1]
    settings = Settings(repo, args.commit, args.tml_dir.expanduser().resolve(),
                        args.saves_dir.expanduser().resolve(), args.state_dir.expanduser().resolve(),
                        args.artifacts_dir.expanduser().resolve(), args.build_only,
                        args.dotnet.expanduser().resolve() if args.dotnet else None)
    try:
        run(settings)
    except (DeploymentError, OSError, subprocess.SubprocessError, tarfile.TarError) as exc:
        print(f"Deployment failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
