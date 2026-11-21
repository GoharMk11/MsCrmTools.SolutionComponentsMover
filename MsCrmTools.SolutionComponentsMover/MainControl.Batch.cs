using McTools.Xrm.Connection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using MsCrmTools.SolutionComponentsMover.AppCode;
using MsCrmTools.SolutionComponentsMover.Forms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using XrmToolBox.Extensibility;

namespace MsCrmTools.SolutionComponentsMover
{
    public partial class MainControl
    {
        private bool batchBusy;
        private volatile bool cancelBatch;
        private int connectionGeneration;
        private string loadedConnectionUrl;
        private string validatedText;
        private List<InputValidationResult> importedResults;
        private BatchPlan batchPreview;

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            cancelBatch = true;
            connectionGeneration++;
            loadedConnectionUrl = null;
            validatedText = null;
            importedResults = null;
            batchPreview = null;
            base.UpdateConnection(newService, detail, actionName, parameter);
            if (txtValidation == null) return;
            sourceSolutionPicker.LoadSolutions(new List<Entity>());
            targetSolutionPicker.LoadSolutions(new List<Entity>());
            txtValidation.Clear();
            lblConnection.Text = "Connection changed. Load solutions again before validation or preview.";
            workspace?.SetTargets(new List<Entity>());
            RefreshWorkspaceSources();
            InvalidatePreview();
        }

        private void WireBatchControls()
        {
            btnImport.Click += (s, e) =>
            {
                using (var dialog = new OpenFileDialog { Filter = "Solution lists (*.csv;*.txt)|*.csv;*.txt" })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        try { txtBatchInput.Text = File.ReadAllText(dialog.FileName); }
                        catch (Exception error) { ShowBatchError(error); }
            };
            btnClearInput.Click += (s, e) =>
            {
                txtBatchInput.Clear();
                importedResults = null;
                validatedText = null;
                txtValidation.Clear();
                RefreshWorkspaceSources();
            };
            btnValidate.Click += (s, e) => ValidateBatchInput();
            btnPreview.Click += (s, e) => PreviewBatch();
            tsbCancel.Click += (s, e) =>
            {
                cancelBatch = true;
                tsbCancel.Enabled = false;
                tslProgress.Text = "Cancelling after the current request; completed changes remain.";
            };
            txtBatchInput.TextChanged += (s, e) =>
            {
                validatedText = null;
                InvalidatePreview();
                txtValidation.Text = "Input changed. Validate & Select again before preview.";
            };
            sourceSolutionPicker.SelectionChanged += (s, e) => InvalidatePreview();
            targetSolutionPicker.SelectionChanged += (s, e) => InvalidatePreview();
            chkCheckBestPractice.CheckedChanged += (s, e) => InvalidatePreview();
        }

        private void InvalidatePreview()
        {
            batchPreview = null;
            workspace?.SetPreview(null);
            if (workspace != null) workspace.PreviewPage.Text = "Preview";
            RefreshWorkspaceSources();
            tsbCopyComponents.Enabled = false;
            UpdateWorkspaceNavigation();
            lblBatchSummary.Text = "Preview required. Target: " + string.Join(", ", targetSolutionPicker.SelectedSolutions
                .Select(s => s.GetAttributeValue<string>("friendlyname")));
        }

        private void SetBatchBusy(bool busy, bool executing = false)
        {
            batchBusy = busy;
            splitContainer2.Enabled = !busy;
            pnlOptions.Enabled = !busy;
            pnlBatch.Enabled = !busy;
            tsbLoadSolutions.Enabled = !busy;
            tsbCloseThisTab.Enabled = !busy;
            clearLogsToolStripMenuItem.Enabled = !busy;
            exportLogsToolStripMenuItem.Enabled = !busy;
            tsbCopyComponents.Enabled = !busy && batchPreview != null;
            tsbCancel.Enabled = busy && executing;
            executionActive = busy && executing;
            UpdateWorkspaceNavigation();
        }

        private bool RequireReady(bool requireValidatedInput)
        {
            if (batchBusy) return false;
            if (Service == null || loadedConnectionUrl == null ||
                !string.Equals(loadedConnectionUrl, ConnectionDetail.WebApplicationUrl, StringComparison.Ordinal))
            {
                ShowBatchError(new InvalidOperationException("Load solutions using the active DEV connection first."));
                return false;
            }
            if (requireValidatedInput && !string.IsNullOrWhiteSpace(txtBatchInput.Text) &&
                (validatedText != txtBatchInput.Text || importedResults == null || importedResults.Any(r => r.Error != null)))
            {
                ShowBatchError(new InvalidOperationException("Resolve every imported input error with Validate & Select, or clear the input to use manual selection."));
                return false;
            }
            return true;
        }

        private void ValidateBatchInput()
        {
            if (!RequireReady(false)) return;
            var text = txtBatchInput.Text;
            var inputs = SolutionInputParser.Parse(text);
            if (inputs.Count == 0) { txtValidation.Text = "Enter at least one solution identifier."; return; }
            InvalidatePreview();
            var manager = sManager;
            var connectedUrl = loadedConnectionUrl;
            var generation = connectionGeneration;
            SetBatchBusy(true);
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Validating imported solutions in the connected organization...",
                Work = (bw, e) => e.Result = manager.ResolveInputs(inputs, connectedUrl),
                PostWorkCallBack = e =>
                {
                    if (IsDisposed || Disposing) return;
                    SetBatchBusy(false);
                    if (generation != connectionGeneration) return;
                    if (e.Error != null) { ShowBatchError(e.Error); return; }
                    importedResults = (List<InputValidationResult>)e.Result;
                    var availableIds = new HashSet<Guid>(managerSolutions.Select(s => s.Id));
                    foreach (var result in importedResults.Where(r => r.Error == null && !availableIds.Contains(r.Solution.Id)))
                        result.Error = "Solution is not in the loaded picker. Reload solutions and validate again.";
                    validatedText = text;
                    sourceSolutionPicker.SelectSolutionsByIds(importedResults.Where(r => r.Error == null).Select(r => r.Solution.Id));
                    var valid = importedResults.Count(r => r.Error == null && !r.Duplicate);
                    var invalid = importedResults.Count(r => r.Error != null);
                    var duplicates = importedResults.Count(r => r.Error == null && r.Duplicate);
                    txtValidation.Text = $"Valid: {valid}  Invalid: {invalid}  Duplicates: {duplicates}" + Environment.NewLine +
                        string.Join(Environment.NewLine, importedResults.Select(r =>
                            $"Line {r.Input.LineNumber}: {r.Input.Text} — " + (r.Error ??
                            ((r.Duplicate ? "Duplicate: " : "Selected: ") + r.Solution.GetAttributeValue<string>("friendlyname")))));
                    RefreshWorkspaceSources();
                }
            });
        }

        private List<Entity> managerSolutions = new List<Entity>();

        private void PreviewBatch()
        {
            if (!RequireReady(true)) return;
            if (sourceSolutionPicker.SelectedSolutions.Count == 0 || targetSolutionPicker.SelectedSolutions.Count != 1)
            {
                ShowBatchError(new InvalidOperationException("Select sources and exactly one unmanaged target."));
                return;
            }
            using (var selector = _solutionComponents != null
                ? new ComponentTypeSelector(_omc, _emc, _solutionComponents)
                : new ComponentTypeSelector(_omc))
            {
                if (selector.ShowDialog(ParentForm) != DialogResult.OK) return;
                var settings = new CopySettings
                {
                    SourceSolutions = sourceSolutionPicker.SelectedSolutions,
                    TargetSolutions = targetSolutionPicker.SelectedSolutions,
                    ComponentsTypes = selector.SelectedComponents,
                    AllComponents = selector.AllItemsSelected,
                    CheckBestPractice = chkCheckBestPractice.Checked,
                    OrganizationMajorVersion = ConnectionDetail.OrganizationMajorVersion
                };
                InvalidatePreview();
                var generation = connectionGeneration;
                var manager = sManager;
                var previewService = Service;
                var previewUrl = loadedConnectionUrl;
                SetBatchBusy(true);
                WorkAsync(new WorkAsyncInfo
                {
                    Message = "Preparing read-only component preview...",
                    Work = (bw, e) =>
                    {
                        var plan = manager.Preview(settings);
                        var identity = (WhoAmIResponse)previewService.Execute(new WhoAmIRequest());
                        plan.OrganizationId = identity.OrganizationId; plan.UserId = identity.UserId; plan.EnvironmentUrl = previewUrl;
                        PreviewNames.Populate(plan, previewService, _omc);
                        ContentSnapshots.Capture(plan, previewService);
                        try { plan.PreviousRun = runHistory.Latest(plan.OrganizationId, plan.Target.Id); }
                        catch (Exception historyError) { plan.Warnings.Add("Previous local report could not be loaded: " + historyError.Message); }
                        e.Result = plan;
                    },
                    PostWorkCallBack = e =>
                    {
                        if (IsDisposed || Disposing) return;
                        SetBatchBusy(false);
                        if (generation != connectionGeneration) return;
                        if (e.Error != null) { ShowBatchError(e.Error); return; }
                        batchPreview = (BatchPlan)e.Result;
                        var summary = $"Sources: {settings.SourceSolutions.Count} | Target: {batchPreview.Target.GetAttributeValue<string>("friendlyname")} " +
                            $"({batchPreview.Target.GetAttributeValue<string>("uniquename")})" + Environment.NewLine +
                            $"Retrieved: {batchPreview.RetrievedCount} | Unique: {batchPreview.Components.Count} | Already Exists: {batchPreview.ExistingCount} | Planned: {batchPreview.PlannedCount}";
                        lblBatchSummary.Text = summary;
                        var previewView = new BatchPreviewForm(batchPreview, true) { TopLevel = false, FormBorderStyle = FormBorderStyle.None };
                        WirePreviewHistory(previewView, batchPreview);
                        workspace.SetPreview(previewView);
                        workspace.PreviewPage.Text = $"Preview ({batchPreview.Components.Count})";
                        workspace.Tabs.SelectedTab = workspace.PreviewPage;
                        UpdateWorkspaceNavigation();
                    }
                });
            }
        }

        private void ExecuteBatch()
        {
            if (!RequireReady(true) || batchPreview == null) return;
            var plan = batchPreview;
            var generation = connectionGeneration;
            var manager = sManager;
            var added = 0;
            var existing = 0;
            var failed = 0;
            var unconfirmed = 0;
            var runReport = RunReport.Create(plan, "Running");
            lastRunReport = runReport;
            cancelBatch = false;
            lvLogs.Items.Clear();
            tslProgress.Text = "0 / " + plan.Components.Count;
            tspBatch.Value = 0;
            workspace.ResultsSummary.Text = "Copy in progress. Completed additions remain if cancelled.";
            workspace.Tabs.SelectedTab = workspace.ResultsPage;
            SetBatchBusy(true, true);
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Copying components sequentially...",
                Work = (bw, e) =>
                {
                    PersistRunReport(runReport);
                    try
                    {
                        manager.CopyComponents(plan, () => cancelBatch, (count, result) =>
                        {
                            runReport.Add(result); PersistRunReport(runReport);
                            bw.ReportProgress(plan.Components.Count == 0 ? 100 : (int)(100.0 * count / plan.Components.Count), Tuple.Create(count, result));
                        }, (attempt, message) => bw.ReportProgress(-1, message));
                    }
                    catch (Exception error) { runReport.Error = error.Message; throw; }
                    finally
                    {
                        runReport.Status = cancelBatch ? "Cancelled" : runReport.Error != null ? "Stopped" : runReport.Results.Any(r => r.Status == "Failed" || r.Status == "Unconfirmed") ? "Finished with issues" : "Finished";
                        runReport.FinishedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                        PersistRunReport(runReport);
                    }
                },
                ProgressChanged = e =>
                {
                    if (generation != connectionGeneration) return;
                    if (e.UserState is string recoveryMessage) { tslProgress.Text = recoveryMessage; return; }
                    var progress = (Tuple<int, ComponentCopyResult>)e.UserState;
                    var result = progress.Item2;
                    tspBatch.Value = e.ProgressPercentage;
                    if (result.Status == "Added") added++;
                    else if (result.Status == "Already Exists") existing++;
                    else if (result.Status == "Unconfirmed") unconfirmed++;
                    else failed++;
                    var item = new ListViewItem(new[] { result.Source, result.DisplayName, result.TypeName, result.Id.ToString(),
                        result.Target, result.Status, result.Error, result.Attempts.ToString(), result.RetryNotes ?? "" });
                    item.ForeColor = result.Status == "Failed" ? Color.Red : result.Status == "Unconfirmed" ? Color.DarkOrange : Color.DarkGreen;
                    lvLogs.Items.Add(item);
                    tslProgress.Text = progress.Item1 + " / " + plan.Components.Count;
                },
                PostWorkCallBack = e =>
                {
                    batchPreview = null;
                    if (IsDisposed || Disposing) return;
                    SetBatchBusy(false);
                    if (generation != connectionGeneration) return;
                    lblBatchSummary.Text = $"{(cancelBatch ? "Cancelled" : e.Error != null ? "Stopped" : "Finished")}: Added {added}, Already Exists {existing}, Failed {failed}, Unconfirmed {unconfirmed}, Not processed {plan.Components.Count - added - existing - failed - unconfirmed}. Completed additions remain. Preview again before re-running.";
                    workspace.ResultsSummary.Text = lblBatchSummary.Text + Environment.NewLine + (runReport.PersistenceWarning ?? "Run report saved locally. Use Export run report for summary, results and the selected plan.");
                    workspace.SetPreview(null);
                    workspace.PreviewPage.Text = "Preview";
                    if (e.Error != null) ShowBatchError(e.Error);
                }
            });
        }

        private void ShowBatchError(Exception error)
        {
            MessageBox.Show(ParentForm, error.Message, "Batch copier", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
