using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using MsCrmTools.SolutionComponentsMover.AppCode;
using MsCrmTools.SolutionComponentsMover.Forms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace MsCrmTools.SolutionComponentsMover
{
    public partial class MainControl : PluginControlBase, IGitHubPlugin, IHelpPlugin
    {
        private EntityMetadataCollection _emc;
        private OptionMetadataCollection _omc;
        private List<Entity> _solutionComponents;
        private SolutionManager sManager;

        public MainControl()
        {
            InitializeComponent();
            InitializeBatchControls();
            WireBatchControls();
        }

        public string HelpUrl => "https://github.com/MscrmTools/MsCrmTools.SolutionComponentsMover/wiki";

        public string RepositoryName => "MsCrmTools.SolutionComponentsMover";

        public string UserName => "MscrmTools";

        public void LoadSolutions()
        {
            if (batchBusy) return;
            InvalidatePreview();
            importedResults = null;
            txtValidation.Clear();
            loadedConnectionUrl = null;
            sourceSolutionPicker.LoadSolutions(new List<Entity>());
            targetSolutionPicker.LoadSolutions(new List<Entity>());
            sManager = new SolutionManager(Service);
            var manager = sManager;
            var service = Service;
            var generation = connectionGeneration;
            var connectionUrl = ConnectionDetail.WebApplicationUrl;
            SetBatchBusy(true);
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading solutions...",
                Work = (bw, e) =>
                {
                    e.Result = manager.RetrieveSolutions();
                    _omc = ((OptionSetMetadata)((RetrieveOptionSetResponse)service.Execute(
                        new RetrieveOptionSetRequest { Name = "componenttype" })).OptionSetMetadata).Options;
                    _emc = MetadataHelper.LoadEntities(service);
                    _solutionComponents = null;
                    // On-premise uses componenttype metadata; do not assume online-only definition support.
                    if (ConnectionDetail.UseOnline && ConnectionDetail.OrganizationMajorVersion >= 9 &&
                        ConnectionDetail.OrganizationMinorVersion >= 1)
                    {
                        _solutionComponents = service.RetrieveMultiple(new QueryExpression("solutioncomponentdefinition")
                        {
                            ColumnSet = new ColumnSet("objecttypecode", "primaryentityname"),
                            Criteria = new FilterExpression
                            {
                                Conditions = { new ConditionExpression("canbeaddedtosolutioncomponents", ConditionOperator.Equal, true) }
                            }
                        }).Entities.ToList();
                    }
                },
                PostWorkCallBack = e =>
                {
                    if (IsDisposed || Disposing) return;
                    SetBatchBusy(false);
                    if (generation != connectionGeneration) return;
                    if (e.Error != null) { ShowBatchError(e.Error); return; }
                    managerSolutions = ((IEnumerable<Entity>)e.Result).ToList();
                    sourceSolutionPicker.LoadSolutions(managerSolutions);
                    targetSolutionPicker.LoadSolutions(managerSolutions);
                    loadedConnectionUrl = connectionUrl;
                    lblConnection.Text = "Connected CRM: " + connectionUrl + " — DEV testing only";
                }
            });
        }

        private void clearLogsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            lvLogs.Items.Clear();
        }

        private void exportLogsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = @"CSV file|*.csv"
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                lvLogs.ToCsv(sfd.FileName);
            }
        }

        private void lvLogs_Resize(object sender, EventArgs e)
        {
            chMessage.Width = lvLogs.Width;
        }

        private void SolutionSelected(object sender, SolutionSelectedEventArgs e)
        {
            Process.Start($"{ConnectionDetail.WebApplicationUrl}/tools/solution/edit.aspx?id={e.Solution.Id}");
        }

        private void tsbCloseThisTab_Click(object sender, EventArgs e)
        {
            CloseTool();
        }

        private void tsbCopyComponents_Click(object sender, EventArgs e)
        {
            ExecuteBatch();
        }

        private void tsbLoadSolutions_Click(object sender, EventArgs e)
        {
            ExecuteMethod(LoadSolutions);
        }
    }
}