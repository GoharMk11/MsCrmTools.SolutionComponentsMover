# Download the Windows DEV test build

[Download latest ZIP — additive sources, Preview filters and named Results](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/raw/refs/heads/feature/batch-solution-import/downloads/Batch-Solution-Components-Copier-Windows-Additive-e55f8f0.zip)

Contains the unchanged Windows CI Release DLL/PDB and English `INSTALL.txt`. Close XrmToolBox, back up the old plugin outside Plugins, replace the DLL and restart. Requires Windows, .NET Framework 4.8 and a compatible XrmToolBox host.

Setup / Preview / Results remain separate. **Add manually...** now preserves imported/manual sources: select additional sources, then click **Add selected sources**. Cancel is read-only; use **Remove selected** on Setup for removal. Preview filters All actions / To add / Existing / Expand combine with Search; they affect display only, never the full copy plan. Results and CSV include component names and readable type labels. All interface/log text is English.

Code commit `e55f8f0`: Windows production Release build 0 warnings / 0 errors, **119 native assertions passed** in [CI](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/actions/runs/44889292197). Tests cover imported-plus-manual single-click regression, cancellation/repeated additions, action/search filtering, unchanged copy plans, named results for success/skip/failure and the actual production UI. [Preview screenshot](preview-filters.png).

Live CRM DEV confirmation is still required. The adjacent `.sha256` checks package integrity. Previous ZIPs remain available for comparison.
