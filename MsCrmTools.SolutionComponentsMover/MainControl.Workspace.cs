using Microsoft.Xrm.Sdk;
using MsCrmTools.SolutionComponentsMover.AppCode;
using MsCrmTools.SolutionComponentsMover.UserControls;
using MsCrmTools.SolutionComponentsMover.Forms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Label = System.Windows.Forms.Label;

namespace MsCrmTools.SolutionComponentsMover
{
    public partial class MainControl
    {
        private BatchWorkspace workspace;
        private Panel workspaceStorage;
        private bool executionActive;
        private readonly RunHistory runHistory = new RunHistory();
        private RunReport lastRunReport;
        private ToolStripButton exportRunReport;

        private void InitializeTabbedWorkspace()
        {
            workspaceStorage = new Panel { Visible = false };
            Controls.Add(workspaceStorage);
            workspaceStorage.Controls.Add(scMain);
            workspaceStorage.Controls.Add(pnlOptions);
            var inputButtons = btnValidate.Parent;
            pnlBatch.Controls.Remove(lblConnection); lblConnection.Dispose();
            pnlBatch.Controls.Remove(lblBatchSummary); workspaceStorage.Controls.Add(lblBatchSummary);
            workspace = new BatchWorkspace(btnPreview, chkCheckBestPractice);
            lblConnection = workspace.ConnectionStatus;
            pnlBatch.Controls.Clear(); pnlBatch.RowStyles.Clear(); pnlBatch.RowCount = 4;
            pnlBatch.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnlBatch.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            pnlBatch.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnlBatch.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            pnlBatch.Controls.Add(new Label { Text = "One Solution URL, GUID or Unique Name per line. Validate before preview.", AutoSize = true }, 0, 0);
            pnlBatch.Controls.Add(txtBatchInput, 0, 1);
            inputButtons.AutoSize = true;
            pnlBatch.Controls.Add(inputButtons, 0, 2);
            pnlBatch.Controls.Add(txtValidation, 0, 3);
            pnlBatch.Dock = DockStyle.Fill;
            workspaceStorage.Controls.Add(pnlBatch);
            workspace.ResultsPage.Controls.Add(lvLogs);
            workspace.ResultsPage.Controls.SetChildIndex(lvLogs, 0);
            Controls.Remove(tsMain); Controls.Add(workspace); Controls.Add(tsMain);
            foreach (ToolStripItem item in tsMain.Items) if (item is ToolStripSeparator) item.Visible = false;
            toolStripLabel1.Visible = false; tsbCloseThisTab.Visible = false;
            workspace.PasteRequested += (s, e) => OpenInputDialog(false);
            workspace.ImportRequested += (s, e) => OpenInputDialog(true);
            workspace.BrowseRequested += (s, e) => OpenManualSources();
            workspace.RemoveRequested += (s, e) =>
            {
                if (batchBusy) return;
                var removed = new HashSet<Guid>(workspace.Sources.SelectedItems.Cast<ListViewItem>().Select(i => ((Entity)i.Tag).Id));
                var keep = sourceSolutionPicker.SelectedSolutions.Where(x => !removed.Contains(x.Id)).Select(x => x.Id).ToList();
                sourceSolutionPicker.ClearSelection(); sourceSolutionPicker.SelectSolutionsByIds(keep);
            };
            workspace.TargetChanged += (s, e) =>
            {
                if (batchBusy) return;
                targetSolutionPicker.ClearSelection();
                if (workspace.SelectedTarget != null) targetSolutionPicker.SelectSolutionsByIds(new[] { workspace.SelectedTarget.Id });
            };
            workspace.Tabs.SelectedIndexChanged += (s, e) => UpdateWorkspaceNavigation();
            exportRunReport = new ToolStripButton("Export run report") { Enabled = false, Visible = false };
            exportRunReport.Click += (s, e) => ExportRunReport();
            tsMain.Items.Add(exportRunReport);
            UpdateWorkspaceNavigation();
        }

        private void UpdateWorkspaceNavigation()
        {
            if (workspace == null) return;
            tsbCopyComponents.Visible = workspace.Tabs.SelectedTab == workspace.PreviewPage;
            tsbCopyComponents.Enabled = !batchBusy && batchPreview != null && batchPreview.PlannedCount > 0;
            tsbCopyComponents.Text = batchPreview == null ? "Copy components" : batchPreview.PlannedCount == 0 ? "Nothing to add" : $"Copy {batchPreview.PlannedCount} components";
            tsbCancel.Visible = executionActive;
            tslProgress.Visible = tspBatch.Visible = executionActive || workspace.Tabs.SelectedTab == workspace.ResultsPage;
            tsddbLogs.Visible = workspace.Tabs.SelectedTab == workspace.ResultsPage;
            workspace.SetupPage.Enabled = !batchBusy;
            if (exportRunReport != null)
            {
                exportRunReport.Visible = workspace.Tabs.SelectedTab == workspace.ResultsPage;
                exportRunReport.Enabled = lastRunReport != null && !batchBusy;
            }
        }

        private void ExportRunReport()
        {
            if (batchBusy || lastRunReport == null) return;
            using (var dialog = new SaveFileDialog { Filter = "Run report ZIP|*.zip", FileName = "run-" + lastRunReport.RunId.ToString("N") + ".zip" })
                if (dialog.ShowDialog(ParentForm) == DialogResult.OK)
                    try { RunHistory.Export(dialog.FileName, lastRunReport); }
                    catch (Exception error) { ShowBatchError(error); }
        }

        private void PersistRunReport(RunReport report)
        {
            try { report.PersistenceWarning = null; runHistory.Save(report); }
            catch (Exception error) { report.PersistenceWarning = "Local history could not be saved: " + error.Message + ". Use Export run report after completion."; }
        }

        private void WirePreviewHistory(BatchPreviewForm view, BatchPlan plan)
        {
            view.SaveBaselineRequested += (s, e) =>
            {
                try
                {
                    var baseline = RunReport.Create(plan, "Preview baseline");
                    baseline.FinishedUtc = baseline.StartedUtc;
                    var path = runHistory.Save(baseline);
                    MessageBox.Show(ParentForm, "Read-only baseline saved locally: " + path, "Baseline saved");
                }
                catch (Exception error) { ShowBatchError(error); }
            };
            view.CompareRequested += (s, e) => ShowRunComparison(plan.PreviousRun, plan);
            view.LoadComparisonRequested += (s, e) =>
            {
                using (var dialog = new OpenFileDialog { Filter = "Run report|*.zip;*.json" })
                    if (dialog.ShowDialog(ParentForm) == DialogResult.OK)
                        try { ShowRunComparison(RunHistory.Load(dialog.FileName), plan); }
                        catch (Exception error) { ShowBatchError(error); }
            };
        }

        private void ShowRunComparison(RunReport previous, BatchPlan plan)
        {
            if (previous == null) return;
            try
            {
                using (var view = new RunComparisonForm(previous, RunReport.Create(plan, "Current preview"))) view.ShowDialog(ParentForm);
            }
            catch (Exception error) { ShowBatchError(error); }
        }

        private void RefreshWorkspaceSources()
        {
            if (workspace == null) return;
            workspace.SetSources(sourceSolutionPicker.SelectedSolutions);
            workspace.ValidationStatus.Text = sourceSolutionPicker.SelectedSolutions.Count + " source(s) selected.";
            if (importedResults != null)
                workspace.ValidationStatus.Text += $" Import: {importedResults.Count(r => r.Error != null)} error(s), {importedResults.Count(r => r.Duplicate && r.Error == null)} duplicate(s). Open Paste sources to review details.";
        }

        private void OpenInputDialog(bool importFile)
        {
            if (batchBusy) return;
            using (var dialog = new Form { Text = "Paste / import source solutions", Width = 950, Height = 570, MinimumSize = new System.Drawing.Size(650, 400), StartPosition = FormStartPosition.CenterParent })
            {
                dialog.Controls.Add(pnlBatch);
                dialog.FormClosing += (s, e) => { if (batchBusy && !IsDisposed && !Disposing) e.Cancel = true; };
                if (importFile) dialog.Shown += (s, e) => btnImport.PerformClick();
                try { dialog.ShowDialog(ParentForm); }
                finally { dialog.Controls.Remove(pnlBatch); workspaceStorage.Controls.Add(pnlBatch); }
            }
            RefreshWorkspaceSources();
        }

        private void OpenManualSources()
        {
            if (!RequireReady(false)) return;
            // Browse in an isolated picker: normal single clicks must not replace imported sources.
            var existing = sourceSolutionPicker.SelectedSolutions.Select(s => s.Id).ToList();
            using (var dialog = new SourceSelectionForm(managerSolutions, existing))
            {
                if (dialog.ShowDialog(ParentForm) == DialogResult.OK)
                    sourceSolutionPicker.SelectSolutionsByIds(dialog.SelectedSourceIds);
            }
            RefreshWorkspaceSources();
        }
    }
}
