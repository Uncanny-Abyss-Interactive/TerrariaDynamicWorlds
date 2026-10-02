"""Failure gates for real-process validation; the companion supplies in-game assertions."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import test_deploy as deployment_tests

deploy = deployment_tests.deploy
package_bytes = deployment_tests.package_bytes

SPEC = importlib.util.spec_from_file_location("live_validate", Path(__file__).resolve().parents[1] / "scripts/live_validate.py")
live = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = live
SPEC.loader.exec_module(live)


class ReportTests(unittest.TestCase):
    def test_stale_and_failed_reports_are_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            report = Path(folder) / "result.json"
            self.assertIsNone(live.read_report(report, "current"))
            for value in ({"run_id": "previous", "phase": "passed"},
                          {"run_id": "current", "phase": "failed", "error": "lost chest"}):
                report.write_text(json.dumps(value))
                with self.assertRaises(live.LiveValidationError):
                    live.read_report(report, "current")

    def test_success_requires_every_named_assertion(self):
        report = {"schema_version": 1, "phase": "passed", "success": True, "checks": {"one": True, "two": True}}
        live.validate_report(report, {"one", "two"}, "passed")
        for changes in ({"success": False}, {"schema_version": 0}, {"checks": {}},
                        {"checks": {"one": True}}, {"checks": {"one": True, "two": False}},
                        {"checks": {"one": True, "two": 1}}):
            with self.subTest(changes=changes), self.assertRaises(live.LiveValidationError):
                live.validate_report(dict(report, **changes), {"one", "two"}, "passed")
        report["checks"] = [{"name": "one", "passed": True}, {"name": "two", "passed": True}]
        live.validate_report(report, {"one", "two"}, "passed")

    def test_early_exit_and_deadline_are_failures(self):
        process = mock.Mock(returncode=7)
        process.poll.return_value = 7
        with self.assertRaisesRegex(live.LiveValidationError, "exited 7"):
            live.wait_for(lambda: None, process, 10, "in-game checks")
        process.poll.return_value = None
        with self.assertRaisesRegex(live.LiveValidationError, "Timed out"):
            live.wait_for(lambda: None, process, 0, "in-game checks")

    def test_fatal_startup_error_fails_without_waiting_for_modal_dialog(self):
        with tempfile.TemporaryDirectory() as folder:
            runtime = Path(folder)
            log = runtime / "client-console.log"
            log.write_text("[Main Thread/FATAL] [tML]: Please ensure Steam is logged in and running.\n")
            process = mock.Mock()
            process.poll.return_value = None
            with self.assertRaisesRegex(live.LiveValidationError, "Steam"):
                live.wait_report(runtime / "result.json", "run", "passed", process, 600, log, runtime)

    def test_failed_context_stops_only_its_owned_process_group(self):
        process = mock.Mock(pid=12345)
        process.poll.return_value = None
        with tempfile.TemporaryDirectory() as folder, mock.patch.object(live.subprocess, "Popen", return_value=process) as popen, \
                mock.patch.object(live.os, "killpg") as kill:
            with self.assertRaisesRegex(RuntimeError, "assertion failed"):
                with live.running_game(["test-runtime"], Path(folder), {}, Path(folder) / "log"):
                    raise RuntimeError("assertion failed")
            self.assertTrue(popen.call_args.kwargs["start_new_session"])
            kill.assert_called_once_with(12345, live.signal.SIGTERM)
            process.wait.assert_called_once_with(timeout=10)
            process.stdin.close.assert_called_once()


class DeploymentGateTests(unittest.TestCase):
    def setUp(self):
        self.fixture = deployment_tests.DeployTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.fixture.create_repo()
        self.settings = self.fixture.settings
        self.settings.dotnet = self.fixture.root / "dotnet"
        self.settings.dotnet.write_bytes(b"runtime")
        self.built = self.fixture.root / "candidate.tmod"
        self.built.write_bytes(package_bytes())

    def test_failed_live_run_blocks_install_and_preserves_existing_state(self):
        before = self.fixture.live.read_bytes()
        receipt = (self.settings.state_dir / "current.json").read_bytes()
        with mock.patch.object(deploy, "build_mod", return_value=self.built), \
                mock.patch.object(live, "validate_live", side_effect=deploy.DeploymentError("live assertion failed")), \
                mock.patch.object(deploy, "deploy_artifact") as install:
            with self.assertRaisesRegex(deploy.DeploymentError, "live assertion failed"):
                deploy.run(self.settings)
            install.assert_not_called()
        self.assertEqual(self.fixture.live.read_bytes(), before)
        self.assertEqual((self.settings.state_dir / "current.json").read_bytes(), receipt)

    def test_validate_only_retains_evidence_without_installing(self):
        self.settings.validate_only = True
        result = {"status": "passed", "run_id": "test"}
        with mock.patch.object(deploy, "build_mod", return_value=self.built), \
                mock.patch.object(live, "validate_live", return_value=result), \
                mock.patch.object(deploy, "deploy_artifact") as install:
            receipt = deploy.run(self.settings)
            install.assert_not_called()
        self.assertEqual(receipt["live_validation"], result)
        self.assertEqual(self.fixture.live.read_bytes(), b"previous live mod")

    def test_partial_server_check_cannot_bypass_graphical_deployment_gate(self):
        self.settings.server_only = True
        with self.assertRaisesRegex(deploy.DeploymentError, "requires --validate-only"):
            deploy.run(self.settings)


if __name__ == "__main__":
    unittest.main()
