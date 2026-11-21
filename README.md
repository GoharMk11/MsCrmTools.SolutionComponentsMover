# Solution Components Mover — batch copier

XrmToolBox plugin for copying multiple source solutions into **one unmanaged target release solution** in the connected Dynamics CRM / Dataverse organization. Source components are never removed. The batch extension targets Dynamics 365 CRM 9.1 On-Premise, retaining .NET Framework 4.8 and the existing production dependencies.

## Windows build and installation

Install Visual Studio 2022 (or Build Tools) with **.NET desktop development**, .NET Framework **4.8 targeting/developer pack**, and NuGet support. Use a compatible XrmToolBox host; the project references `XrmToolBoxPackage 1.2025.10.74` and `MscrmTools.Xrm.Connection 1.2025.9.64`.

From a Visual Studio Developer PowerShell at the repository root:

```powershell
msbuild .\MsCrmTools.SolutionComponentsMover.sln /restore /p:Configuration=Release '/p:Platform=Any CPU'
```

The existing post-build step moves the plugin DLL/PDB into:

```text
MsCrmTools.SolutionComponentsMover\bin\Release\Plugins\
```

Close XrmToolBox, copy the plugin DLL (and optionally its PDB) into the host's active Plugins folder, and restart. Use XrmToolBox's folder/settings UI to locate the active folder. Debug by attaching Visual Studio to the running XrmToolBox process with matching symbols. No CRM server plugin registration, credentials in code or server modifications are needed.

The Windows validation workflow also uploads the Release plugin as `batch-copier-release`. The old `.nuspec` still contains historical net452/package metadata: do not treat it as a validated release package for this extension. Local DLL builds do not require the signing credentials used by the legacy Azure pipeline.

## Usage

1. Connect XrmToolBox to **DEV**, open Solution Components Mover and select **Load solutions**.
2. On **Setup**, click **Paste sources...** or **Import CSV/TXT...**. Enter one HTTPS Solution edit URL, GUID or Unique Name per line and use **Validate & Select**. Review every error in that dialog. Headerless single-column CSV/TXT is supported; multi-column/multiline CSV is unsupported. Close the dialog after reviewing results; input is preserved for later editing.
3. Alternatively use **Add manually...**, choose additional sources (Ctrl/Shift for multiple), then click **Add selected sources**. This is additive: existing imported/manual sources are preserved even after a normal single click in the dialog. Cancel leaves selection unchanged. Setup displays only selected sources. **Remove selected** removes highlighted source selections. Imported sources preserve existing manual choices.
4. Choose exactly one unmanaged **Target release solution** in the searchable dropdown. Typing alone is not a valid target choice; select an existing entry. The target must differ from all sources.
5. **Advanced settings** contains the enabled-by-default best-practice check. Changes to input, selection, target, settings or connection invalidate the existing preview.
6. Click **Preview components**, choose component types, and review the **Preview** tab's searchable read-only table, source/target names and Add / Expand / Skip actions. Technical IDs and inclusion behavior are available through the checkbox. The **All actions / To add / Existing / Expand** dropdown combines with search and shows the visible row count. These filters affect display only; copying always uses the full reviewed plan.
7. On Preview, explicitly click **Copy N components** in the toolbar. If all coverage is satisfied, the button says **Nothing to add** and is disabled. No copy starts from import or tab navigation.
8. The **Results** tab shows sequential progress, per-component outcomes (including component names and readable types) and totals. **Cancel remaining** stops after the current request; completed additions remain and no rollback occurs. Use **Logs → Export logs** on Results for CSV. Create a fresh preview before retrying.

Dependencies are not automatically added (`AddRequiredComponents=false`). The best-practice check blocks managed entities included with all assets in unmanaged sources; disable it only after deliberately reviewing those entities. Environment variable definitions retain the original rule excluding implicit values/settings.

## Build, run reports, change detection and history

- The connection header displays **Version / Build**. CI embeds the exact Git revision in the DLL; the downloadable build name and exported report identify that same revision. Local builds without `/p:BuildRevision=<sha>` display `local`.
- Every Copy execution starts a local JSON record, checkpoints component results and records its final status. History is stored under `%APPDATA%\MscrmTools\XrmToolBox\SolutionComponentsMover\History`. **Export run report** on Results saves a ZIP with `summary.txt`, `results.csv` and `run.json`: organization/user IDs, source/target names and IDs, type selection, settings, timestamps, build, warnings, outcomes and attempts. Reports contain hashes, not raw scripts/XML or credentials. If local saving fails, a visible warning is shown and the in-memory report remains exportable.
- Preview captures supported content/selected fields for Web Resources, forms, views, workflows, plugin assemblies/types/steps. A tracked change or failed revalidation stops Copy and requires a new preview; earlier completed additions remain. Technical details show the tracking method, including timestamp-only fallback, unavailable and unsupported cases. Entity/field metadata and unselected dependencies/secure configuration are not comprehensively fingerprinted. This is a best-effort guard, not an immutable export or lock; changes can still occur after a check.
- Temporary network/known CRM busy faults allow at most **3 write attempts**, using cancellable backoff and honoring server retry delays. Target coverage is checked before repeating a failed request. A timeout after a confirmed commit needs no repeat write. Permission/business faults are not retried; an unverifiable result is **Unconfirmed**, never falsely called successful. Review the target and create a fresh preview for such outcomes. Attempts and retry notes are in Results/exports. Server retry delays above 60 seconds stop that component instead of retrying early.
- **Compare previous run** on Preview uses the latest local record for the same organization/target IDs. **Save baseline** stores a read-only preview when no earlier execution exists, including the Nothing to add case. **Load report to compare...** accepts an exported report ZIP or local history JSON. Comparison shows added/removed plan entries, changed inclusion and available fingerprints. It does not remove components from CRM and is not a code-line diff. Different organization/target IDs are rejected.

## Tests and verification limits

With .NET SDK 8 installed, run native tests on Windows:

```powershell
dotnet build .\tests\BatchCopier.Tests\BatchCopier.Tests.csproj -f net48 --configuration Release
& .\tests\BatchCopier.Tests\bin\Release\net48\BatchCopier.Tests.exe
```

Portable logic checks can be run using `dotnet run --project tests/BatchCopier.Tests -f net8.0`. They use the actual CRM SDK types with a fake `IOrganizationService`; the framework-fallback warning on net8 is expected. They never connect to CRM. `dotnet test` is not the runner for this console assertion suite.

**Real Windows/XrmToolBox and CRM 9.1 DEV integration testing is required before acceptance.** See [implementation record and DEV checklist](docs/BATCH-IMPLEMENTATION.md) for phase-by-phase changes, tested behavior and unverified server behavior.
