import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from bump_version import prepare_updates


class BumpVersionTests(unittest.TestCase):
    def setUp(self):
        self.versions = """<Project>
  <PropertyGroup>
    <!-- This repo version -->
    <MajorVersion>17</MajorVersion>
    <MinorVersion>0</MinorVersion>
    <PatchVersion>0</PatchVersion>
    <VersionPrefix>$(MajorVersion).$(MinorVersion).$(PatchVersion)</VersionPrefix>
    <PreReleaseVersionLabel>preview.1</PreReleaseVersionLabel>
    <AspireDashboardImageTag>13.6</AspireDashboardImageTag>
    <StabilizePackageVersion>false</StabilizePackageVersion>
  </PropertyGroup>
</Project>
"""
        self.policy = """configuration:
  resourceManagementConfiguration:
    eventResponderTasks:
    - if:
      - payloadType: Pull_Request
      - isAction:
          action: Closed
      - targetsBranch:
          branch: main
      then:
      - addMilestone:
          milestone: 17.0
      description: 'Main milestone'
    - if:
      - payloadType: Pull_Request
      - isAction:
          action: Closed
      - targetsBranch:
          branch: release/13.6
      then:
      - removeMilestone
      - addMilestone:
          milestone: 13.6
      description: 'Release milestone'
"""

    def test_updates_only_requested_properties_and_main_milestone(self):
        versions, policy = prepare_updates(self.versions, self.policy, "18.2.3", "main", "18.2")
        expected = self.versions.replace("<MajorVersion>17</MajorVersion>", "<MajorVersion>18</MajorVersion>")
        expected = expected.replace("<MinorVersion>0</MinorVersion>", "<MinorVersion>2</MinorVersion>")
        expected = expected.replace("<PatchVersion>0</PatchVersion>", "<PatchVersion>3</PatchVersion>")
        self.assertEqual(expected, versions)
        self.assertEqual(self.policy.replace("milestone: 17.0", "milestone: 18.2"), policy)

    def test_resets_patch_and_is_idempotent(self):
        initial = prepare_updates(self.versions, self.policy, "18.2.3", "main", "18.2")
        result = prepare_updates(*initial, "19.0", "main", "19.0")
        self.assertIn("<PatchVersion>0</PatchVersion>", result[0])
        self.assertIn("<MinorVersion>0</MinorVersion>", result[0])
        self.assertEqual(result, prepare_updates(*result, "19.0", "main", "19.0"))

    def test_release_branch_does_not_change_main_rule(self):
        versions, policy = prepare_updates(self.versions, self.policy, "13.6.1", "release/13.6", "13.6.x")
        self.assertEqual(self.policy.replace("milestone: 13.6", "milestone: 13.6.x"), policy)
        self.assertIn("<PatchVersion>1</PatchVersion>", versions)

    def test_preserves_crlf(self):
        inputs = (self.versions.replace("\n", "\r\n"), self.policy.replace("\n", "\r\n"))
        result = prepare_updates(*inputs, "18.0", "main", "18.0")
        expected = prepare_updates(self.versions, self.policy, "18.0", "main", "18.0")
        self.assertEqual(tuple(text.replace("\n", "\r\n") for text in expected), result)

    def test_rejects_invalid_arguments(self):
        for version, branch, milestone in (
            ("18", "main", "18.0"),
            ("18.0-preview.1", "main", "18.0"),
            ("018.0", "main", "18.0"),
            ("18.0", "main\n", "18.0"),
            ("18.0", "main", "18.0\nother: value"),
            ("18.0", "missing", "18.0"),
        ):
            with self.subTest(version=version, branch=branch, milestone=milestone):
                with self.assertRaises(ValueError):
                    prepare_updates(self.versions, self.policy, version, branch, milestone)

    def test_rejects_unexpected_file_structure(self):
        for versions, policy in (
            (self.versions.replace("<MajorVersion>17</MajorVersion>", ""), self.policy),
            (self.versions.replace("</Project>", "<MajorVersion>17</MajorVersion></Project>"), self.policy),
            (self.versions.replace("$(MajorVersion).$(MinorVersion).$(PatchVersion)", "17.0.0"), self.policy),
            (self.versions, self.policy + self.policy),
            (self.versions, self.policy.replace("milestone: 17.0", "milestone: future")),
        ):
            with self.subTest(versions=versions, policy=policy):
                with self.assertRaises(ValueError):
                    prepare_updates(versions, policy, "18.0", "main", "18.0")

    def test_cli_validates_before_writing_and_resolves_its_own_repository(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            script = root / ".agents/skills/bump-aspire-version/bump_version.py"
            script.parent.mkdir(parents=True)
            shutil.copyfile(Path(__file__).with_name("bump_version.py"), script)
            paths = (root / "eng/Versions.props", root / ".github/policies/milestoneAssignment.prClosed.yml")
            for path, content in zip(paths, (self.versions, self.policy)):
                path.parent.mkdir(parents=True)
                path.write_bytes(content.encode("utf-8"))

            command = [sys.executable, "-B", str(script), "18.0", "--milestone", "18.0", "--branch"]
            result = subprocess.run(command + ["missing"], capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("Expected exactly one milestone policy task", result.stderr)
            self.assertEqual((self.versions, self.policy), tuple(p.read_text() for p in paths))

            expected = prepare_updates(self.versions, self.policy, "18.0", "main", "18.0")
            for _ in range(2):
                result = subprocess.run(command + ["main"], capture_output=True, text=True)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(expected, tuple(p.read_text() for p in paths))


if __name__ == "__main__":
    unittest.main()
