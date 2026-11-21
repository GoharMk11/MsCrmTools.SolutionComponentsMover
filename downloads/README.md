# Download the Windows DEV test build

[Download latest ZIP — build identity, reports, change guards, retries and comparison](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/raw/refs/heads/feature/batch-solution-import/downloads/Batch-Solution-Components-Copier-Windows-Operations-3722c6a.zip)

Close XrmToolBox, back up the old plugin outside Plugins, replace the DLL from the ZIP and restart. Confirm **Build 3722c6a** in the header. Windows/.NET Framework 4.8 and a compatible XrmToolBox host are required. The ZIP contains unchanged CI DLL/PDB plus English installation/test instructions.

The tabbed UI retains additive source selection, action filters and named Results. It now adds:

- Exact compiled build revision in the UI and reports.
- Local JSON run history with source/target IDs, settings, build/timestamps and outcomes/attempts; **Export run report** produces summary.txt, results.csv and run.json in a ZIP.
- Supported content/selected-field hashes checked before writes, with explicit unsupported/unavailable/timestamp-only coverage. This is a best-effort guard, not a frozen solution export or lock.
- At most 3 attempts for narrowly identified temporary write faults, cancellation-aware backoff and target verification before retry. Unconfirmed outcomes require review; permanent faults are not retried.
- Read-only **Save baseline**, **Compare previous run**, and **Load report to compare...** for the same CRM organization/target IDs. Removed plan entries never remove CRM components.

Code commit `3722c6a`: Windows production Release build 0 warnings / 0 errors, **160 native assertions passed** in [CI](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/actions/runs/44894348279). [Setup screenshot](operations-setup.png) · [Preview screenshot](operations-preview.png).

Live CRM 9.1 DEV validation of the new reads/transient conditions remains required. Entity/field metadata and unselected dependencies/secure configuration are not comprehensively fingerprinted. Reports contain hashes rather than raw component payloads. All tool UI/log text is English. Original dependency/inclusion request flags and production dependency versions are unchanged. Earlier ZIPs remain available; the adjacent .sha256 verifies this package.
