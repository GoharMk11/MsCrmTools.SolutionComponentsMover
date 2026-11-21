using Microsoft.Xrm.Sdk;
using MsCrmTools.SolutionComponentsMover.UserControls;
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
            using (var dialog = new Form { Text = "Select source solutions — Ctrl/Shift selects multiple", Width = 1000, Height = 620, StartPosition = FormStartPosition.CenterParent })
            {
                sourceSolutionPicker.Dock = DockStyle.Fill;
                dialog.Controls.Add(sourceSolutionPicker);
                try { dialog.ShowDialog(ParentForm); }
                finally { dialog.Controls.Remove(sourceSolutionPicker); workspaceStorage.Controls.Add(sourceSolutionPicker); }
            }
            RefreshWorkspaceSources();
        }
    }
}
