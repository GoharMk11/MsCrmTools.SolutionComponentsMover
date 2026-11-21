# Download the Windows DEV test build

[Download ZIP](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/raw/refs/heads/feature/batch-solution-import/downloads/Batch-Solution-Components-Copier-Windows-Table-Preview-20445a5.zip)

Contains the unchanged Windows CI Release DLL/PDB and `INSTALL.txt` with installation, first-test and plugin restoration instructions. Use Windows, .NET Framework 4.8 and a compatible XrmToolBox host. Open **Solution Components Mover** in XrmToolBox and use a disposable DEV connection.

Code commit `20445a5`; Windows Release build and 86 native assertions passed in [CI](https://github.com/GoharMk11/MsCrmTools.SolutionComponentsMover/actions/runs/44883950188). Real CRM DEV integration is still pending. The adjacent `.sha256` file checks package integrity.

The latest build includes a searchable, read-only preview table with friendly names, explicit actions and optional technical columns. All tool UI/log text is English. [View the Windows preview screenshot](preview-table.png). The previous ZIP remains available for comparison.
