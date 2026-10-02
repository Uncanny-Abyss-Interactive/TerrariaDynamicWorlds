import os
from pathlib import Path
import subprocess
import unittest


class RunnerGuardTests(unittest.TestCase):
    def guard(self, **overrides):
        repo = "Uncanny-Abyss-Interactive/TerrariaDynamicWorlds"
        env = dict(os.environ, GITHUB_REPOSITORY=repo,
                   GITHUB_REF="refs/heads/main", GITHUB_EVENT_NAME="push",
                   GITHUB_WORKFLOW_REF=repo + "/.github/workflows/deploy-tmodloader.yml@refs/heads/main")
        env.update(overrides)
        script = Path(__file__).resolve().parents[1] / "scripts/runner-guard.sh"
        return subprocess.run(["bash", str(script)], env=env, capture_output=True).returncode

    def test_main_push_and_manual_run_allowed(self):
        self.assertEqual(self.guard(), 0)
        self.assertEqual(self.guard(GITHUB_EVENT_NAME="workflow_dispatch"), 0)

    def test_untrusted_job_contexts_rejected(self):
        for override in (
            {"GITHUB_EVENT_NAME": "pull_request"},
            {"GITHUB_EVENT_NAME": "pull_request_target"},
            {"GITHUB_REF": "refs/heads/feature"},
            {"GITHUB_REPOSITORY": "someone/fork"},
            {"GITHUB_WORKFLOW_REF": "another-workflow"},
            {"GITHUB_WORKFLOW_REF": ""},
        ):
            with self.subTest(override=override):
                self.assertNotEqual(self.guard(**override), 0)


if __name__ == "__main__":
    unittest.main()
