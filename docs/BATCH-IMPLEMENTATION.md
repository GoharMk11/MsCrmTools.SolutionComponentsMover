# Batch copier implementation record

## Phase 1 — baseline

- Fork remote: `GoharMk11/MsCrmTools.SolutionComponentsMover`; starting commit `a0ec240`.
- One classic .NET Framework 4.8 WinForms library, hosted by XrmToolBox. Existing SDK requests and connection are retained.
- Existing manual source/target pickers and component type selector are the extension points.
- No baseline test project. Linux environment initially has no dotnet, MSBuild, Mono or Windows runtime; a native Windows baseline build cannot be run here.
- No CRM connection or DEV credentials are configured. No CRM server has been contacted.
- Feature branch: `feature/batch-solution-import`. Changes must be reviewed and tested in DEV before merging.

## Phase 2 — input parsing

- Files: `AppCode/SolutionInputParser.cs`, `tests/BatchCopier.Tests/Program.cs`.
- HTTPS URLs, plain/braced GUIDs, Unique Names and headerless single-column CSV/TXT are supported. Whitespace lines are ignored; invalid nonblank rows remain errors. URL user info, fragments, duplicate/missing/invalid IDs, wrong paths and non-HTTPS URLs are rejected.
- Host, effective port and normalized organization path must match the active connection. Inputs are resolved against that organization's `IOrganizationService`; mixed GUID/name references to the same solution are reported as duplicates.
- Parser tests pass as part of the 64-assertion portable suite, including 12 pasted URLs and per-line validation.
- Limits: multi-column CSV and multiline CSV cells are deliberately unsupported and reported as invalid input; no network URL fetching occurs.

## Phase 3 — bulk selection UI

- Files: `UserControls/SolutionPicker.cs`, `MainControl.cs`, `MainControl.Batch.cs`, `MainControl.Designer.cs`, project compile entries.
- Adds paste/import, Validate & Select, Clear input, connected-organization display and per-row validation output. Existing manual sources are preserved. Selected entries stay visible through filtering; import clears the filter. Target selection is single-select.
- Filtering now runs on the UI thread, avoiding the former `Thread.Abort` race. Validation resolves IDs before selection and does not execute add requests. Unresolved input errors block preview/execution until fixed or cleared.
- Added native Windows picker regression tests for manual selection, imported selection, filter visibility, manual deselection, single target and reload. They exercise the actual WinForms control. Full XrmToolBox layout still requires interaction testing.

## Phases 4–5 — preview and copy planning

- Files: `AppCode/SolutionManager.cs`, `AppCode/BatchPlan.cs`, `AppCode/CopySettings.cs`, `Forms/ComponentTypeSelector.cs`.
- Preview resolves selected solutions again, rejects managed targets, multiple targets and target-as-source, reads source/target components using explicit first-page initialization and paging cookies, checks problematic managed entities, and builds one operation per component type/ID.
- Inclusion policy: `0` (all assets) covers `1` (exclude subcomponents) and `2` (shell); `1` covers `2`. Merging sources uses the broadest requested behavior. An entity with missing inclusion behavior blocks preview rather than assuming all assets. For other types, missing versus explicit inclusion behavior is ambiguous and blocks preview rather than silently dropping a requirement. Unrecognized behaviors are rejected. Existing target components are skipped only when their coverage satisfies the request.
- Environment Variable Definition (380) retains the existing special rule: exclude implicit values/settings, and plan its coverage as exclusion rather than all assets. Value components (381) are copied only when explicitly present and selected. No uniform inclusion rule is imposed on other component types.
- The original on-premise metadata route is retained; online definition metadata is only requested on the supported online route. Missing entity labels no longer crash the type selector.
- Preview displays source names, target name/unique name, raw/unique/satisfied/planned counts, warnings and every planned component with behavior and origins. Preview issues no writes.
- Tests cover paging, source duplicates, coverage expansion, filtered types, target validation, managed-entity error names, request flags and source membership preservation.
- Limits: the coverage policy is tested with a fake service using actual SDK request/entity types, not verified against CRM 9.1 server behavior. In particular, shell/selected assets, relationships, managed entities and implicit additions require DEV validation.

## Phase 6 — execution and reporting

- Files: `MainControl.Batch.cs`, `MainControl.Designer.cs`, `AppCode/ListViewToCsv.cs`, `AppCode/CsvFormatting.cs`.
- Copy All requires the reviewed preview. The engine revalidates and rereads CRM immediately before writes, rejecting changed plans. Components execute sequentially on the existing XrmToolBox worker; each SDK add failure is recorded and other components continue. `AddRequiredComponents=false` is retained.
- Progress bar/count, cancellation of remaining work, concurrent-click protection, preview invalidation after input/selection/connection changes and connection-change cancellation are implemented. An in-flight request cannot be rolled back or interrupted safely. Completed additions remain.
- Logs include all source unique names for shared components, type, ID, target, status and error. Totals distinguish Added, Already Exists, Failed and Not processed. CSV quotes embedded quotes/newlines and includes UTF-8 BOM. Unprocessed components are counted, not falsely logged as successful.
- Tests cover failures, cancellation, stale preview, repeated execution and CSV field formatting. The CSV UI export itself awaits Windows testing.

## Phase 7 — validation and deployment status

- Added `tests/BatchCopier.Tests` (console assertions, exit code fails on assertion failure) targeting both net8.0 and net48. Production remains .NET Framework 4.8 with unchanged package versions. Tests link the actual engine/parser sources and use actual CRM SDK types; no CRM server is contacted.
- Linux net8 run: **64 assertions passed**. NU1701 is expected because the CRM SDK package targets .NET Framework; this is not proof of native runtime compatibility.
- Full C# source compile against net48 and the production dependencies in an external temporary compile project: **0 warnings / 0 errors**. This check excludes embedded resources and Windows post-build commands and is not a deployable plugin build.
- Native net48 test executable compilation passed on Linux. Windows CI subsequently passed the production Release build and native tests, including the actual WinForms picker.
- The original project's Linux full build stops at MSB3823/MSB3822 for non-string WinForms resources. Production resources/framework/dependencies were not changed to work around Linux limitations.
- `.github/workflows/batch-copier-windows.yml` builds the real Release solution, runs net48 tests on Windows and uploads `bin/Release/Plugins` as an artifact. Observed successful Windows runs: [initial native build/tests](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/actions/runs/43490652906) and [71 assertions including the picker](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/actions/runs/43490924235). The final unknown-entity-coverage regression adds one assertion; its final run is recorded below.
- **Not verified:** real XrmToolBox load, UI layout, CRM 9.1 On-Premise DEV integration, dependency behavior, forms/views/fields, workflows/plugin components and server-side repeat/cancellation behavior. No DEV connection or Windows desktop was provided. No CRM environment, including PROD, was accessed.
- Acceptance is **pending** these integration checks; compilation and fake tests alone do not complete acceptance.
- The fork's actual default branch is `master`, and `main` does not exist. PR base awaits the user's choice; no default branch is created or changed implicitly.

## Required DEV acceptance checklist

Use a disposable DEV organization and 2–3 small unmanaged sources plus a separate unmanaged release target.

- [ ] Load the built DLL in a compatible XrmToolBox and inspect the UI at normal/high DPI.
- [ ] Paste at least 10 entries, including aliases/duplicates, invalid URLs, wrong organizations and missing names; verify every result and preserved manual selections.
- [ ] Ensure preview does not change any solution and displays the correct target and exact plan.
- [ ] Exercise shared tables with shell/selected/all-assets behavior, explicit fields/forms/views and broader/narrower pre-existing target coverage. Compare exported solution contents before/after.
- [ ] Check workflows, plugin assemblies/steps, dependencies and a deliberately failing component.
- [ ] Verify definition-only environment variables do not gain values; explicitly selected values copy correctly where supported by the server.
- [ ] Cancel mid-batch, confirm completed additions remain, unprocessed work stops and totals/CSV are accurate.
- [ ] Repeat successful batches and confirm no unnecessary add requests; failed components remain retryable.
- [ ] Switch connections after preview and verify reload/repreview is required.
- [ ] Verify source memberships remain unchanged and exactly one unmanaged target is used.

### Final observed Windows result

Code commit `33aa280` passed [Windows CI run 43491205219](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/actions/runs/43491205219): production Release build **0 warnings / 0 errors**, native test executable **72 assertions passed** (64 parser/engine/CSV assertions plus 8 actual WinForms picker assertions). Test restore also reports the documented net8 framework-fallback warning. The [Release DLL/PDB artifact](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/actions/runs/43491205219/artifacts/14069423950) was uploaded successfully. The final documentation-only commit skips CI; no production code changed after the tested commit. Actual XrmToolBox hosting and CRM DEV acceptance remain unverified.


## Readable table preview update

- Replaced the plain text preview with a resizable, read-only WinForms table showing Component name, Type, Source solution(s), and Planned action. Actions distinguish Add to target, Expand existing coverage and Skip — already included.
- A header identifies the target, source names and counts, with separate Add/Expand/Skip totals and combined source duplicate count. Search matches names, types, origins, actions and IDs. Technical details (GUID and inclusion behavior) are hidden initially and can be enabled explicitly.
- All tool UI and log text remains English, as requested.
- Presentation-only batched queries resolve Web Resource names and common forms/views/workflows/plugin components. Unsupported or inaccessible names show an explicit fallback with IDs in technical details; name lookup faults do not block or alter the copy plan. Reads use the existing connection and run off the UI thread.
- Copy requests, inclusion rules, dependency handling and source membership preservation are unchanged.
- Portable suite passes 71 assertions. Windows tests additionally cover the real table's read-only behavior, labels, technical columns and search, and capture a sample preview screenshot. Final Windows evidence and download package are recorded after CI.
- User-provided evidence confirms the prior read-only preview retrieved 19 memberships / 18 unique Web Resources across three DEV sources; the new friendly-name queries still require user DEV confirmation.

### Table preview Windows validation

Code commit `20445a5` passed [Windows CI](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/actions/runs/44883950188): production Release build 0 warnings / 0 errors and **86 native assertions passed**. The actual preview form was rendered on Windows and its [screenshot](../downloads/preview-table.png) visually inspected. The [download package](../downloads/Batch-Solution-Components-Copier-Windows-Table-Preview-20445a5.zip) contains the unchanged CI DLL/PDB and updated English installation instructions. Names and read-only queries were checked with a fake service; friendly-name retrieval against the user's CRM still awaits their test. Copy behavior is unchanged.
