"""Validate suite artifacts, retain usable reports, and describe incomplete runs."""

import argparse
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET


def expected_projects(repository):
    # src/Directory.Build.props marks Eventuous.Tests* projects as tests unless
    # the project explicitly opts out (the shared test fixture libraries do).
    return sorted(
        project.stem
        for project in (repository / "src").rglob("Eventuous.Tests*.csproj")
        if not any(
            (value.text or "").strip().lower() == "false"
            for value in ET.parse(project).getroot().iter("IsTestProject")
        )
    )


def prepare(repository, artifacts, destination, projects=None):
    expected = expected_projects(repository) if projects is None else projects
    destination.mkdir(parents=True, exist_ok=True)
    inputs = destination / "inputs"
    inputs.mkdir(exist_ok=True)
    for old in inputs.glob("*.cobertura.xml"):
        old.unlink()

    rows = []
    valid_count = 0
    healthy = bool(expected)
    for name in expected:
        reports = list(artifacts.glob(f"**/{name}/coverage.cobertura.xml"))
        status = "Missing coverage"
        duration = "—"
        if len(reports) > 1:
            status = "Duplicate coverage inputs"
        elif len(reports) == 1:
            report = reports[0]
            try:
                root = ET.parse(report).getroot()
                if root.tag != "coverage" or not root.findall(".//class/lines/line"):
                    raise ValueError("No instrumented source lines")
                shutil.copyfile(report, inputs / f"{name}.cobertura.xml")
                valid_count += 1
                metadata = json.loads((report.parent / "run.json").read_text())
                if type(metadata["exit_code"]) is not int or type(metadata["duration_seconds"]) is not int:
                    raise ValueError("Invalid run metadata")
                duration = f'{metadata["duration_seconds"]}s'
                status = "Passed" if metadata["exit_code"] == 0 else f'Test command failed (exit {metadata["exit_code"]})'
            except (ET.ParseError, OSError, ValueError, KeyError, TypeError) as error:
                status = f"Invalid or missing report/metadata: {error}"
        healthy &= status == "Passed"
        rows.append(f"| {name} | {status.replace('|', '/').replace(chr(10), ' ')} | {duration} |")

    label = "Complete" if healthy else "Incomplete or failed run"
    summary = [
        f"# Coverage: {label}", "",
        f"Usable coverage from **{valid_count}/{len(expected)}** expected .NET 10 test projects.", "",
        "Percentages describe only the available reports; missing suites can change the result.", "",
        "| Test project | Status | Test command duration (including build) |",
        "|---|---|---|", *rows, "",
        "Timing is instrumented command time, not measured coverage overhead. Compare equivalent CI runs to assess overhead.", "",
        "Shared source compiled into multiple assemblies is counted separately in each assembly.", "",
    ]
    (destination / "status.md").write_text("\n".join(summary))
    # A missing/failed collection fails the reporting job, but valid inputs remain
    # available for the always() report-generation and artifact-upload steps.
    return 0 if healthy else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--artifacts", type=Path, default=Path("coverage-results"))
    parser.add_argument("--output", type=Path, default=Path("coverage-report"))
    parser.add_argument("--project", action="append", help="Limit expected projects for a focused local run; omit in CI")
    args = parser.parse_args()
    raise SystemExit(prepare(args.repository, args.artifacts, args.output, args.project))
