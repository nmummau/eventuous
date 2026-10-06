# Code coverage

Pull requests and pushes to `dev` collect coverage during the existing .NET 10 test runs in `.github/workflows/tests.yml`. The .NET 8 and .NET 9 jobs still run correctness tests without collection. Provider tests remain in separate jobs; coverage does not add another test pass or a whole-solution test job.

The collection extension comes from the pinned TUnit package (currently TUnit 1.63.0, which depends on `Microsoft.Testing.Extensions.CodeCoverage` 18.9.0). ReportGenerator is pinned in `.config/dotnet-tools.json`. Collection uses the [native Microsoft.Testing.Platform coverage options](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage).

## Reports in Actions

The **Coverage report** job publishes an Actions summary and a **Coverage Report** artifact. Download and extract the artifact, read `status.md` for suite completeness, and open the included `index.html` to browse source coverage. The artifact also contains a merged Cobertura report. Raw **Coverage Inputs** artifacts retain per-project coverage and command timings. All coverage artifacts have a 14-day retention period.

The report includes line and branch coverage by assembly. No coverage percentage threshold is enforced. Test failures still fail their jobs. Missing, duplicate, malformed, or empty coverage reports fail validation; usable reports are still published with an **incomplete or failed run** title. A failed test command's coverage can be retained, but the failure is shown explicitly. A canceled workflow may not finish reporting.

Expected projects follow the same convention as `src/Directory.Build.props`: `src/**/Eventuous.Tests*.csproj`, excluding projects that explicitly set `IsTestProject` to `false`. When adding a test project, add it to the appropriate existing CI job. The reporting tests check that the workflow schedules all expected projects.

Coverage artifacts are separate from **Test Results** artifacts. The test-result publishing workflow only reads XML/TRX under the latter, so it does not treat Cobertura as test results. The coverage job needs only read access to repository contents and uses artifacts from the same run; it does not require secrets, PR comments, or additional write permissions.

## Local collection

From the repository root, with .NET 10 and Python 3 installed:

```bash
bash scripts/test-with-coverage.sh src/Core/test/Eventuous.Tests.Application/Eventuous.Tests.Application.csproj
python3 scripts/prepare-coverage.py --project Eventuous.Tests.Application
dotnet tool restore
dotnet tool run reportgenerator \
  '-reports:coverage-report/inputs/*.cobertura.xml' \
  '-targetdir:coverage-report/html' \
  '-reporttypes:Html;MarkdownSummaryGithub;Cobertura' \
  '-assemblyfilters:+Eventuous*;-Eventuous.Tests*;-Eventuous.Sut*;-Eventuous.TestHelpers*'
```

Open `coverage-report/html/index.html`. The collection script defaults to `Debug CI`; set `CONFIGURATION` to use another configuration. Additional `dotnet test` options such as `--no-build --no-restore` can follow the project argument after an initial build. `COVERAGE_OUTPUT_DIR` optionally changes the raw output directory.

To merge more projects, run the collection script for each and repeat `--project` for each expected project when preparing the report. Omit `--project` to require every suite, as CI does. A local subset is not a solution-wide baseline. Provider projects need their usual container prerequisites.

Run reporting regression tests with:

```bash
python3 -B -m unittest discover -s scripts -p 'test_*.py'
```

## Interpreting shared source

Collection includes Eventuous production assemblies and excludes test assemblies, SUT fixtures, and test helpers. Filters are defined in `.github/coverage.config.xml` and repeated at report generation.

Shared source files compiled into several assemblies represent distinct runtime code in each assembly. An uncovered copy of a helper in one provider does not mean another provider's copy was never tested. Reports merge executions of the same assembly across test suites while preserving separate assembly identities. Inspect the assembly and behavioral scenario before deciding a coverage gap needs a test.

Compiler-generated sources that were not written to disk may have coverage counters but no browsable source text. ReportGenerator can warn about these missing generated files; the available source reports are still produced.

## Runtime overhead

Each project's `run.json` and the Actions summary record the instrumented test command duration, including its build. This is not a direct measurement of collection overhead. Compare equivalent CI runs on the same runner image and commit, with comparable cache state, including artifact upload and the final report job. Parallel job durations should not be added together as an estimate of workflow wall time.

A local Linux sample using SDK 10.0.201, the 21 application tests, `Debug CI`, and `--no-build --no-restore` measured three alternating runs per mode:

| Mode | Runs (seconds) | Median |
|---|---|---|
| Without coverage | 1.920, 1.873, 2.021 | 1.920 s |
| With native coverage | 4.126, 3.925, 4.125 | 4.125 s |

That is about 2.2 seconds of additional time for this small warm suite. It does not predict provider-suite or full CI overhead. Measure the first comparable hosted runs before setting an overhead budget. If full coverage materially delays PR feedback, consider a separate change to collect full reports only on `dev` or a schedule.
