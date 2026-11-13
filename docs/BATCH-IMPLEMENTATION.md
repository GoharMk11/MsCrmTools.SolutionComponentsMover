# Batch copier implementation record

## Phase 1 — baseline

- Fork remote: `GoharMk11/MsCrmTools.SolutionComponentsMover`; starting commit `a0ec240`.
- One classic .NET Framework 4.8 WinForms library, hosted by XrmToolBox. Existing SDK requests and connection are retained.
- Existing manual source/target pickers and component type selector are the extension points.
- No baseline test project. Linux environment initially has no dotnet, MSBuild, Mono or Windows runtime; a native Windows baseline build cannot be run here.
- No CRM connection or DEV credentials are configured. No CRM server has been contacted.
- Feature branch: `feature/batch-solution-import`. Changes must be reviewed and tested in DEV before merging.
