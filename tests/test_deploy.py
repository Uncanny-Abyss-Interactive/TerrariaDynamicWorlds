"""Run with: python3 -m unittest discover -s tests -v"""

import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


SPEC = importlib.util.spec_from_file_location("dw_deploy", Path(__file__).resolve().parents[1] / "scripts/deploy.py")
deploy = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = deploy
SPEC.loader.exec_module(deploy)


def dotnet_string(value):
    data = value.encode("utf-8")
    assert len(data) < 128
    return bytes([len(data)]) + data


def package_bytes(version="0.5.0"):
    content = b"test assembly content"
    payload = (dotnet_string("DynamicWorlds") + dotnet_string(version) + struct.pack("<i", 1)
               + dotnet_string("DynamicWorlds.dll") + struct.pack("<ii", len(content), len(content)) + content)
    return (b"TMOD" + dotnet_string("2026.2.3.0") + hashlib.sha1(payload).digest()
            + bytes(256) + struct.pack("<i", len(payload)) + payload)


class DeployTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.settings = deploy.Settings(self.root / "repo", "HEAD", self.root / "tml",
                                        self.root / "saves", self.root / "state", self.root / "artifacts")
        self.settings.tml_dir.mkdir()
        (self.settings.tml_dir / "tModLoader.dll").write_bytes(b"test loader")
        self.live = self.settings.saves_dir / "Mods/DynamicWorlds.tmod"
        self.live.parent.mkdir(parents=True)
        self.live.write_bytes(b"previous live mod")
        self.settings.state_dir.mkdir()
        (self.settings.state_dir / "current.json").write_text('{"git_sha":"previous"}\n')

    def git(self, *args):
        return subprocess.run(["git", *args], cwd=self.settings.repo, text=True,
                              capture_output=True, check=True).stdout.strip()

    def create_repo(self):
        self.settings.repo.mkdir()
        self.git("init", "-q")
        source = self.settings.repo / "DynamicWorlds"
        source.mkdir()
        (source / "build.txt").write_text("version = 0.5.0\n")
        (source / "Example.cs").write_text("// committed source\n")
        self.git("add", ".")
        self.git("-c", "user.name=Deployment Test", "-c", "user.email=test@example.invalid",
                 "commit", "-qm", "fixture")
        return source, self.git("rev-parse", "HEAD")

    def test_archive_uses_requested_commit_and_preserves_dirty_tree(self):
        source, first_commit = self.create_repo()
        (source / "Example.cs").write_text("// newer commit\n")
        self.git("add", ".")
        self.git("-c", "user.name=Deployment Test", "-c", "user.email=test@example.invalid",
                 "commit", "-qm", "new revision")
        (source / "Example.cs").write_text("// uncommitted work\n")
        (source / "Untracked.cs").write_text("// untracked\n")
        destination = self.root / "extract"
        destination.mkdir()
        archived = deploy.archive_source(self.settings.repo, first_commit, destination)
        self.assertEqual((archived / "Example.cs").read_text(), "// committed source\n")
        self.assertFalse((archived / "Untracked.cs").exists())
        self.assertEqual((source / "Example.cs").read_text(), "// uncommitted work\n")
        self.assertTrue((source / "Untracked.cs").exists())

    def test_failed_builder_preserves_live_mod_and_receipt(self):
        self.create_repo()
        runtime = self.settings.tml_dir / "dotnet/dotnet"
        runtime.parent.mkdir()
        runtime.write_text(f"#!{sys.executable}\nimport sys\nprint('intentional compiler error')\nsys.exit(7)\n")
        runtime.chmod(0o755)
        before = self.live.read_bytes()
        receipt_before = (self.settings.state_dir / "current.json").read_bytes()
        with self.assertRaisesRegex(deploy.DeploymentError, "exit 7"):
            deploy.run(self.settings)
        self.assertEqual(self.live.read_bytes(), before)
        self.assertEqual((self.settings.state_dir / "current.json").read_bytes(), receipt_before)
        self.assertFalse((self.settings.state_dir / "backups").exists())
        self.assertIn("intentional compiler error", next(self.settings.artifacts_dir.glob("*/build.log")).read_text())

    def test_build_only_builds_committed_source_and_never_touches_live_state(self):
        source, commit = self.create_repo()
        (source / "Example.cs").write_text("// dirty working tree\n")
        runtime = self.settings.tml_dir / "dotnet/dotnet"
        runtime.parent.mkdir()
        runtime.write_text(
            f"#!{sys.executable}\n"
            "from pathlib import Path\nimport os, sys\n"
            "assert sys.argv[1:4] == ['tModLoader.dll', '-server', '-build']\n"
            "source = Path(sys.argv[4])\n"
            "assert (source / 'Example.cs').read_text() == '// committed source\\n'\n"
            "assert sys.argv[5] == '-tmlsavedirectory'\n"
            "assert os.environ['DOTNET_ROLL_FORWARD'] == 'Disable'\n"
            "mods = Path(sys.argv[6]) / 'Mods'\nmods.mkdir(parents=True)\n"
            f"(mods / 'DynamicWorlds.tmod').write_bytes({package_bytes()!r})\n"
            "print('fake build finished')\n"
        )
        runtime.chmod(0o755)
        self.settings.build_only = True
        (self.settings.saves_dir / "Mods/enabled.json").write_text('["ExistingMod"]')
        world = self.settings.saves_dir / "Worlds/example.wld"
        world.parent.mkdir()
        world.write_bytes(b"world must remain unchanged")
        before = {str(path): path.read_bytes() for path in self.settings.saves_dir.rglob("*") if path.is_file()}
        receipt_before = (self.settings.state_dir / "current.json").read_bytes()
        receipt = deploy.run(self.settings)
        self.assertEqual(receipt["git_sha"], commit)
        self.assertEqual(receipt["mod_version"], "0.5.0")
        self.assertEqual(receipt["tmodloader_dll_sha256"], deploy.sha256(self.settings.tml_dir / "tModLoader.dll"))
        self.assertEqual(receipt["artifact_sha256"], deploy.sha256(Path(receipt["artifact_path"])))
        self.assertEqual(before, {str(path): path.read_bytes() for path in self.settings.saves_dir.rglob("*") if path.is_file()})
        self.assertEqual((self.settings.state_dir / "current.json").read_bytes(), receipt_before)
        self.assertEqual((source / "Example.cs").read_text(), "// dirty working tree\n")
        self.assertFalse((self.settings.state_dir / "backups").exists())

    def test_atomic_deploy_backs_up_previous_artifact_and_receipt(self):
        artifact = self.root / "DynamicWorlds.tmod"
        artifact.write_bytes(package_bytes())
        receipt = dict(deploy.validate_tmod(artifact, "0.5.0"), git_sha="a" * 40,
                       artifact_sha256=deploy.sha256(artifact), tmodloader_dll_sha256="b" * 64)
        previous = self.live.read_bytes()
        previous_receipt = (self.settings.state_dir / "current.json").read_bytes()
        with mock.patch.object(deploy, "ensure_game_stopped"):
            with mock.patch.object(deploy.os, "replace", wraps=deploy.os.replace) as replace:
                result = deploy.deploy_artifact(self.settings, artifact, receipt)
        self.assertEqual(self.live.read_bytes(), artifact.read_bytes())
        self.assertEqual(deploy.sha256(self.live), result["artifact_sha256"])
        backup = Path(result["backup_dir"])
        self.assertEqual((backup / self.live.name).read_bytes(), previous)
        self.assertEqual((backup / "current.json").read_bytes(), previous_receipt)
        self.assertEqual(json.loads((self.settings.state_dir / "current.json").read_text()), result)
        self.assertEqual(replace.call_args_list[0].args[1], self.live)
        self.assertEqual(replace.call_args_list[0].args[0].parent, self.live.parent)
        self.assertEqual(len(replace.call_args_list), 2)
        self.assertFalse(list(self.live.parent.glob(".dynamic-worlds-*")))

    def test_receipt_failure_rolls_back_live_mod(self):
        artifact = self.root / "artifact.tmod"
        artifact.write_bytes(package_bytes())
        previous = self.live.read_bytes()
        real_replace = deploy.os.replace

        def failing_receipt_replace(source, target):
            if target == self.settings.state_dir / "current.json":
                raise OSError("receipt write failed")
            return real_replace(source, target)

        with mock.patch.object(deploy, "ensure_game_stopped"), \
                mock.patch.object(deploy.os, "replace", side_effect=failing_receipt_replace):
            with self.assertRaisesRegex(OSError, "receipt write failed"):
                deploy.deploy_artifact(self.settings, artifact, {"git_sha": "a" * 40,
                                                                "artifact_sha256": deploy.sha256(artifact)})
        self.assertEqual(self.live.read_bytes(), previous)
        self.assertEqual(json.loads((self.settings.state_dir / "current.json").read_text())["git_sha"], "previous")

    def test_installed_hash_mismatch_rolls_back_before_receipt_publication(self):
        artifact = self.root / "artifact.tmod"
        artifact.write_bytes(package_bytes())
        previous = self.live.read_bytes()
        previous_receipt = (self.settings.state_dir / "current.json").read_bytes()
        with mock.patch.object(deploy, "ensure_game_stopped"):
            with self.assertRaisesRegex(deploy.DeploymentError, "Installed mod hash"):
                deploy.deploy_artifact(self.settings, artifact, {"artifact_sha256": "wrong hash"})
        self.assertEqual(self.live.read_bytes(), previous)
        self.assertEqual((self.settings.state_dir / "current.json").read_bytes(), previous_receipt)

    def test_running_game_blocks_deployment_before_backup(self):
        artifact = self.root / "artifact.tmod"
        artifact.write_bytes(package_bytes())
        with mock.patch.object(deploy, "active_tml_processes", return_value=["PID 123: dotnet tModLoader.dll"]):
            with self.assertRaisesRegex(deploy.DeploymentError, "Quit tModLoader"):
                deploy.deploy_artifact(self.settings, artifact, {})
        self.assertEqual(self.live.read_bytes(), b"previous live mod")
        self.assertFalse((self.settings.state_dir / "backups").exists())

    def test_process_detection_ignores_unrelated_dotnet_and_build_process(self):
        executable_listing = "10 /path with spaces/dotnet\n11 /path/dotnet\n12 /path/dotnet\n13 /bin/bash"
        arguments = ("10 /path with spaces/dotnet tModLoader.dll -server\n"
                     "11 /path/dotnet tModLoader.dll -server -build /tmp/source\n"
                     "12 /path/dotnet unrelated.dll\n")
        with mock.patch.object(deploy, "command_output", return_value=executable_listing), \
                mock.patch.object(deploy.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, arguments, "")):
            self.assertEqual(deploy.active_tml_processes(), ["PID 10: /path with spaces/dotnet tModLoader.dll -server"])

    def test_corrupt_or_wrong_version_package_is_rejected(self):
        artifact = self.root / "artifact.tmod"
        valid = package_bytes()
        for data in (b"bad", valid[:-1], valid[:-1] + bytes([valid[-1] ^ 1])):
            artifact.write_bytes(data)
            with self.assertRaises(deploy.DeploymentError):
                deploy.validate_tmod(artifact, "0.5.0")
        artifact.write_bytes(valid)
        with self.assertRaisesRegex(deploy.DeploymentError, "Expected DynamicWorlds 0.6.0"):
            deploy.validate_tmod(artifact, "0.6.0")

    def test_overlapping_deploy_is_rejected(self):
        with deploy.deployment_lock(self.settings.state_dir):
            with self.assertRaisesRegex(deploy.DeploymentError, "Another Dynamic Worlds"):
                with deploy.deployment_lock(self.settings.state_dir):
                    self.fail("Second lock unexpectedly acquired")


if __name__ == "__main__":
    unittest.main()
