#if NET48
using Microsoft.Xrm.Sdk;
using MsCrmTools.SolutionComponentsMover.UserControls;
using MsCrmTools.SolutionComponentsMover.Forms;
using MsCrmTools.SolutionComponentsMover.AppCode;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static class WorkspaceTests
{
    public static void Run(Action<bool, string> check)
    {
        using (var form = new Form { Width = 1050, Height = 750 })
        using (var workspace = new BatchWorkspace(new Button(), new CheckBox { Text = "Check best practice before copying components", Checked = true }))
        {
            form.Controls.Add(workspace); form.Show();
            var source = Solution("Source A");
            var target = Solution("Release Test");
            var managed = Solution("Managed"); managed["ismanaged"] = true;
            workspace.SetSources(new[] { source });
            workspace.SetTargets(new[] { target, managed });
            check(workspace.Tabs.TabPages.Cast<TabPage>().Select(p => p.Text).SequenceEqual(new[] { "Setup", "Preview", "Results" }), "three clear workspace tabs");
            check(workspace.Sources.Items.Count == 1 && workspace.Sources.Items[0].Text == "Source A", "only selected sources are listed");
            check(workspace.Target.Items.Count == 1 && workspace.SelectedTarget == null, "unmanaged targets only and no automatic target choice");
            workspace.Target.SelectedIndex = 0;
            check(workspace.SelectedTarget.Id == target.Id, "dropdown resolves one explicit target");
            workspace.Target.Text = "unknown target";
            check(workspace.SelectedTarget == null, "free text cannot retain a stale target");
            check(!workspace.AdvancedPanel.Visible, "advanced options collapsed initially");
            workspace.AdvancedToggle.Checked = true;
            check(workspace.AdvancedPanel.Visible, "advanced options can be opened");
            workspace.AdvancedToggle.Checked = false;
            var plan = new BatchPlan { Settings = new CopySettings { SourceSolutions = new List<Entity> { source } }, Target = target, RetrievedCount = 0, Components = new List<PlannedComponent>() };
            var preview = new BatchPreviewForm(plan, true) { TopLevel = false, FormBorderStyle = FormBorderStyle.None };
            workspace.SetPreview(preview); workspace.Tabs.SelectedTab = workspace.PreviewPage;
            check(preview.Visible && preview.Parent == workspace.PreviewHost, "preview embeds into its tab without modal dialog");
            workspace.SetPreview(null);
            check(preview.IsDisposed && workspace.PreviewEmpty.Visible, "invalidating a plan disposes the stale preview");
            workspace.Tabs.SelectedTab = workspace.SetupPage;
            workspace.Target.SelectedIndex = -1;
            workspace.Target.SelectedIndex = 0;
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace-setup.png"), ImageFormat.Png);
            }
            workspace.Tabs.SelectedTab = workspace.ResultsPage;
            workspace.SetSources(new[] { source, target });
            workspace.Tabs.SelectedTab = workspace.SetupPage;
            check(workspace.Sources.Items.Count == 2 && workspace.SelectedTarget?.Id == target.Id, "tab navigation retains selections");
            plan.OrganizationId = Guid.NewGuid();
            var oldPlan = RunReport.Create(plan, "Preview baseline");
            plan.Components.Add(new PlannedComponent { Id = Guid.NewGuid(), Type = 61, DisplayName = "new.js", Sources = new List<string> { "SourceA" } });
            using (var comparison = new RunComparisonForm(oldPlan, RunReport.Create(plan, "Current preview")))
            {
                comparison.Show();
                check(comparison.Grid.ReadOnly && comparison.Grid.Rows.Count == 1 && comparison.Grid.Rows[0].Cells[0].Value.ToString() == "Added to plan", "comparison window displays read-only list differences");
                comparison.Close();
            }
            form.Close();
        }
    }

    internal static Entity Solution(string name)
    {
        return new Entity("solution", Guid.NewGuid()) { ["friendlyname"] = name, ["uniquename"] = name.Replace(" ", ""), ["ismanaged"] = false,
            ["publisherid"] = new EntityReference("publisher", Guid.NewGuid()) { Name = "Test publisher" }, ["version"] = "1.0", ["installedon"] = DateTime.Today };
    }
}
#endif
