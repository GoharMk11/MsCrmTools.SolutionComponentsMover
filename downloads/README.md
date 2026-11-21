# Download the Windows DEV test build

[Download latest ZIP — Setup / Preview / Results](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/raw/refs/heads/feature/batch-solution-import/downloads/Batch-Solution-Components-Copier-Windows-Tabs-aee3ca3.zip)

Contains the unchanged Windows CI Release DLL/PDB and English `INSTALL.txt`. Close XrmToolBox, back up the old plugin outside the Plugins folder, replace the DLL and restart. Windows, .NET Framework 4.8 and a compatible XrmToolBox installation are required.

The new workspace separates Setup, Preview and Results. Setup shows selected sources and a searchable unmanaged target; paste/import and manual source browsing open separate dialogs. Preview contains the read-only component table and Copy N components action. Results contains progress, cancellation, per-component results and CSV export. All interface/log text is English.

Code commit `aee3ca3`: Windows Release build 0 warnings / 0 errors, **104 native assertions passed** in [CI](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/actions/runs/44885892171). Tests include constructing the actual production DLL UI offline, source/target synchronization, input dialog lifetime and busy-state protection. [See the real Windows UI screenshot](tabs-setup.png).

CRM 9.1 On-Premise DEV integration still requires user confirmation. The adjacent `.sha256` checks package integrity. Earlier ZIP builds remain available for comparison.
