using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using MsCrmTools.SolutionComponentsMover.AppCode;
using System;
using System.Collections.Generic;
using System.Linq;
#if NET48
using MsCrmTools.SolutionComponentsMover.Forms;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
#endif

internal static class PreviewTests
{
    public static void Run(Action<bool, string> check)
    {
        var source = new Entity("solution", Guid.NewGuid()); source["friendlyname"] = "Source A";
        var target = new Entity("solution", Guid.NewGuid()); target["friendlyname"] = "Release"; target["uniquename"] = "release";
        var component = new PlannedComponent { Id = Guid.NewGuid(), Type = 61, Behavior = 0, Sources = new List<string> { "SourceA", "SourceB" } };
        var plan = new BatchPlan { Target = target, Settings = new CopySettings { SourceSolutions = new List<Entity> { source } }, RetrievedCount = 2, Components = new List<PlannedComponent> { component } };
        var signature = plan.Signature;
        var service = new NamesService();
        PreviewNames.Populate(plan, service, null);
        check(component.DisplayName == "new_/scripts/customer.js" && component.TypeName == "Web Resource", "readable web resource name and type");
        check(plan.Signature == signature, "name lookup does not change copy semantics");
        check(component.ActionLabel == "Add to target", "new membership action");
        component.TargetPresent = true;
        check(component.ActionLabel == "Expand existing coverage", "existing narrow membership action");
        component.AlreadyExists = true;
        check(component.ActionLabel == "Skip — already included", "already satisfied action");
        service.Fail = true;
        PreviewNames.Populate(plan, service, null);
        check(component.NameWarning.Contains("test name failure") && component.AlreadyExists, "name failure is isolated from copy state");
        service.Fail = false;
        plan.Components = Enumerable.Range(0, 401).Select(i => new PlannedComponent { Id = Guid.NewGuid(), Type = 61 }).ToList();
        service.Calls = 0;
        PreviewNames.Populate(plan, service, null);
        check(service.Calls == 3 && service.MaxIds == 200 && plan.Components.All(c => c.DisplayName != null), "name lookup batches IDs");
        plan.Components = new List<PlannedComponent> { component };
#if NET48
        using (var form = new BatchPreviewForm(plan))
        {
            form.Show();
            check(form.Grid.ReadOnly && !form.Grid.AllowUserToAddRows && form.Grid.Rows.Count == 1, "preview grid is read-only");
            check(Convert.ToString(form.Grid.Rows[0].Cells["Component"].Value) == component.DisplayName, "grid shows component name");
            check(!form.Grid.Columns["Id"].Visible && !form.Grid.Columns["Behavior"].Visible, "technical fields hidden initially");
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-table.png"), ImageFormat.Png);
            }
            form.Technical.Checked = true;
            check(form.Grid.Columns["Id"].Visible && form.Grid.Columns["Behavior"].Visible, "technical fields toggle");
            form.Search.Text = "not a match";
            check(!form.Grid.Rows[0].Visible, "preview search filters rows");
            form.Search.Text = "sourceb";
            check(form.Grid.Rows[0].Visible, "search matches secondary source name");
            check(Convert.ToString(form.Grid.Rows[0].Cells["Action"].Value) == "Skip — already included", "grid action is explicit");
            form.Close();
        }
        var newComponent = new PlannedComponent { Id = Guid.NewGuid(), Type = 61, DisplayName = "new.js", TypeName = "Web Resource", Sources = new List<string> { "SourceA" } };
        var expanded = new PlannedComponent { Id = Guid.NewGuid(), Type = 61, DisplayName = "expand.js", TypeName = "Web Resource", TargetPresent = true, Sources = new List<string> { "SourceA" } };
        plan.Components = new List<PlannedComponent> { component, newComponent, expanded };
        var beforeFilter = plan.Signature;
        using (var filtered = new BatchPreviewForm(plan))
        {
            filtered.Show();
            Func<int> visible = () => filtered.Grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Count(r => r.Visible);
            check(visible() == 3, "all action filter shows complete preview");
            filtered.ActionFilter.SelectedIndex = 1;
            check(visible() == 1 && filtered.Grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Single(r => r.Visible).Cells["Component"].Value.ToString() == "new.js", "to-add filter excludes existing and expansion rows");
            filtered.ActionFilter.SelectedIndex = 2;
            check(visible() == 1 && filtered.Grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Single(r => r.Visible).Cells["Action"].Value.ToString().StartsWith("Skip"), "existing filter shows skipped components");
            filtered.ActionFilter.SelectedIndex = 3;
            check(visible() == 1 && filtered.Grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Single(r => r.Visible).Cells["Component"].Value.ToString() == "expand.js", "expand filter shows coverage expansions");
            filtered.Search.Text = "new.js";
            check(visible() == 0 && filtered.VisibleCount.Text == "Showing 0 of 3", "action and text filters combine with visible count");
            filtered.ActionFilter.SelectedIndex = 1;
            check(visible() == 1 && plan.Signature == beforeFilter && plan.PlannedCount == 2, "view filtering never narrows the copy plan");
            filtered.Close();
        }
#endif
    }

    private sealed class NamesService : IOrganizationService
    {
        public bool Fail;
        public int Calls;
        public int MaxIds;
        public EntityCollection RetrieveMultiple(QueryBase queryBase)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("test name failure");
            var query = (QueryExpression)queryBase;
            if (query.EntityName != "webresource") throw new Exception("unexpected table");
            var ids = query.Criteria.Conditions[0].Values.Cast<Guid>().ToList();
            MaxIds = Math.Max(MaxIds, ids.Count);
            return new EntityCollection(ids.Select(id => new Entity("webresource", id) { ["name"] = "new_/scripts/customer.js" }).ToList());
        }
        public Entity Retrieve(string name, Guid id, ColumnSet columns) => throw new NotSupportedException();
        public OrganizationResponse Execute(OrganizationRequest request) => throw new Exception("Preview must not execute writes");
        public Guid Create(Entity entity) => throw new NotSupportedException();
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string name, Guid id) => throw new NotSupportedException();
        public void Associate(string name, Guid id, Relationship relationship, EntityReferenceCollection entities) => throw new NotSupportedException();
        public void Disassociate(string name, Guid id, Relationship relationship, EntityReferenceCollection entities) => throw new NotSupportedException();
    }
}
