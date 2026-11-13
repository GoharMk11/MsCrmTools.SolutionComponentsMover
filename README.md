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
2. Select sources manually, or paste one HTTPS Solution edit URL, GUID or Unique Name per line. **Import CSV/TXT** accepts the same headerless one-column format (quoted fields are supported). Multi-column/multiline CSV is unsupported.
3. Select **Validate & Select**. Review every row; fix all invalid inputs. GUIDs/names resolve only in the active organization; URL host, port and organization must match it. Existing manual selections are retained. **Clear input** clears import validation but retains selected sources.
4. Choose one unmanaged **Target Release Solution** that is not a source.
5. Select **Preview Components**, choose component types and review counts, source names, target, inclusion behaviors and planned operations. Preview is read-only. Changing input, source/target selection, best-practice settings or connection invalidates it.
6. Select **Copy All** explicitly. Work is sequential and errors are recorded individually. **Cancel remaining** stops after the current request; completed additions remain and there is no rollback.
7. Review totals and use **Logs → Export logs** to export the six result columns. Run preview again before retrying. Already-satisfied target coverage is skipped; narrower coverage is scheduled for expansion.

Dependencies are not automatically added (`AddRequiredComponents=false`). The best-practice check blocks managed entities included with all assets in unmanaged sources; disable it only after deliberately reviewing those entities. Environment variable definitions retain the original rule excluding implicit values/settings.

## Tests and verification limits

With .NET SDK 8 installed, run native tests on Windows:

```powershell
dotnet build .\tests\BatchCopier.Tests\BatchCopier.Tests.csproj -f net48 --configuration Release
& .\tests\BatchCopier.Tests\bin\Release\net48\BatchCopier.Tests.exe
```

Portable logic checks can be run using `dotnet run --project tests/BatchCopier.Tests -f net8.0`. They use the actual CRM SDK types with a fake `IOrganizationService`; the framework-fallback warning on net8 is expected. They never connect to CRM. `dotnet test` is not the runner for this console assertion suite.

**Real Windows/XrmToolBox and CRM 9.1 DEV integration testing is required before acceptance.** See [implementation record and DEV checklist](docs/BATCH-IMPLEMENTATION.md) for phase-by-phase changes, tested behavior and unverified server behavior.
