import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("prepare_coverage", Path(__file__).with_name("prepare-coverage.py"))
coverage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(coverage)

REPORT = '<coverage><packages><package name="Eventuous.Domain"><classes><class name="State" filename="State.cs"><lines><line number="1" hits="1"/></lines></class></classes></package></packages></coverage>'


class CoverageReportingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.artifacts = self.root / "artifacts"
        self.output = self.root / "report"

    def report(self, name="Eventuous.Tests", artifact="Coverage Inputs core", exit_code=0):
        folder = self.artifacts / artifact / name
        folder.mkdir(parents=True, exist_ok=True)
        (folder / "coverage.cobertura.xml").write_text(REPORT)
        (folder / "run.json").write_text(json.dumps({"exit_code": exit_code, "duration_seconds": 12}))
        return folder

    def prepare(self, projects=None):
        return coverage.prepare(self.root, self.artifacts, self.output, projects or ["Eventuous.Tests"])

    def test_complete_report_records_duration_and_copies_usable_input(self):
        self.report()
        self.assertEqual(self.prepare(), 0)
        self.assertIn("Coverage: Complete", (self.output / "status.md").read_text())
        self.assertIn("Passed | 12s", (self.output / "status.md").read_text())
        self.assertEqual((self.output / "inputs/Eventuous.Tests.cobertura.xml").read_text(), REPORT)

    def test_missing_suite_fails_but_retains_other_suites(self):
        self.report()
        self.assertEqual(self.prepare(["Eventuous.Tests", "Eventuous.Tests.Sqlite"]), 1)
        summary = (self.output / "status.md").read_text()
        self.assertIn("1/2", summary)
        self.assertIn("Eventuous.Tests.Sqlite | Missing coverage", summary)
        self.assertTrue((self.output / "inputs/Eventuous.Tests.cobertura.xml").exists())

    def test_failed_tests_retain_their_coverage_and_do_not_look_successful(self):
        self.report(exit_code=2)
        self.assertEqual(self.prepare(), 1)
        self.assertIn("Test command failed (exit 2)", (self.output / "status.md").read_text())
        self.assertTrue((self.output / "inputs/Eventuous.Tests.cobertura.xml").exists())

    def test_invalid_or_empty_xml_is_not_passed_to_report_generator(self):
        folder = self.report()
        for text in ("not XML", "<coverage/>"):
            with self.subTest(text=text):
                (folder / "coverage.cobertura.xml").write_text(text)
                self.assertEqual(self.prepare(), 1)
                self.assertEqual(list((self.output / "inputs").iterdir()), [])

    def test_duplicate_suite_cannot_be_merged_as_if_it_were_one_run(self):
        self.report()
        self.report(artifact="Coverage Inputs extra")
        self.assertEqual(self.prepare(), 1)
        self.assertIn("Duplicate coverage", (self.output / "status.md").read_text())
        self.assertEqual(list((self.output / "inputs").iterdir()), [])

    def test_missing_run_metadata_is_reported_while_coverage_remains_available(self):
        folder = self.report()
        (folder / "run.json").unlink()
        self.assertEqual(self.prepare(), 1)
        self.assertIn("Invalid or missing", (self.output / "status.md").read_text())
        self.assertTrue((self.output / "inputs/Eventuous.Tests.cobertura.xml").exists())

    def test_rerun_removes_stale_report_inputs(self):
        folder = self.report()
        self.assertEqual(self.prepare(), 0)
        (folder / "coverage.cobertura.xml").unlink()
        self.assertEqual(self.prepare(), 1)
        self.assertEqual(list((self.output / "inputs").iterdir()), [])

    def test_collection_preserves_test_failure_and_rejects_missing_coverage(self):
        repository = Path(__file__).resolve().parents[1]
        project = self.root / "Eventuous.Tests.Fake.csproj"
        project.write_text("<Project/>")
        # Exercise the shell entry point without a build: the stand-in test runner
        # either emits a report and fails, or succeeds without emitting coverage.
        runner = self.root / "dotnet"
        runner.write_text("""#!/usr/bin/env python3
import os, sys
from pathlib import Path
if os.environ['FAKE_REPORT'] == 'yes':
    Path(sys.argv[sys.argv.index('--coverage-output') + 1]).write_text('<coverage/>')
raise SystemExit(int(os.environ['FAKE_EXIT']))
""")
        runner.chmod(0o755)
        output = self.root / "collected"
        for report, runner_exit, expected_exit in (("yes", 2, 2), ("no", 0, 1)):
            with self.subTest(report=report, runner_exit=runner_exit):
                env = dict(os.environ, PATH=str(self.root) + os.pathsep + os.environ["PATH"],
                           COVERAGE_OUTPUT_DIR=str(output), FAKE_REPORT=report, FAKE_EXIT=str(runner_exit))
                result = subprocess.run(["bash", str(repository / "scripts/test-with-coverage.sh"), str(project)],
                                        env=env, capture_output=True, text=True)
                self.assertEqual(result.returncode, expected_exit, result.stderr)
                metadata = json.loads((output / project.stem / "run.json").read_text())
                self.assertEqual(metadata["exit_code"], runner_exit)
                self.assertEqual((output / project.stem / "coverage.cobertura.xml").exists(), report == "yes")

    def test_discovery_matches_projects_actually_scheduled_in_ci(self):
        repository = Path(__file__).resolve().parents[1]
        workflow = (repository / ".github/workflows/tests.yml").read_text()
        scheduled = {Path(path).stem for path in re.findall(r"src/[^\s\"']+\.csproj", workflow)}
        self.assertEqual(set(coverage.expected_projects(repository)), scheduled)


if __name__ == "__main__":
    unittest.main()
