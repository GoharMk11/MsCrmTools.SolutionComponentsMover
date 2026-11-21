using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Label = System.Windows.Forms.Label;

namespace MsCrmTools.SolutionComponentsMover.UserControls
{
    internal sealed class BatchWorkspace : UserControl
    {
        internal TabControl Tabs { get; } = new TabControl { Dock = DockStyle.Fill };
        internal TabPage SetupPage { get; } = new TabPage("Setup");
        internal TabPage PreviewPage { get; } = new TabPage("Preview");
        internal TabPage ResultsPage { get; } = new TabPage("Results");
        internal Label ConnectionStatus { get; } = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = "Load solutions using a DEV connection.", Padding = new Padding(8) };
        internal Label ValidationStatus { get; } = new Label { AutoSize = true, Text = "No sources selected.", Padding = new Padding(0, 8, 0, 4) };
        internal ListView Sources { get; } = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
        internal ComboBox Target { get; } = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        internal Panel AdvancedPanel { get; } = new Panel { Dock = DockStyle.Fill, Height = 36, Visible = false };
        internal CheckBox AdvancedToggle { get; } = new CheckBox { AutoSize = true, Text = "Advanced settings", Padding = new Padding(0, 6, 0, 6) };
        internal Panel PreviewHost { get; } = new Panel { Dock = DockStyle.Fill };
        internal Label ResultsSummary { get; } = new Label { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8), Text = "No copy has been run. Results will appear here after Copy All." };
        internal Label PreviewEmpty { get; } = new Label { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(16), Text = "Choose sources and a target on Setup, then click Preview components." };
        internal event EventHandler PasteRequested;
        internal event EventHandler ImportRequested;
        internal event EventHandler BrowseRequested;
        internal event EventHandler RemoveRequested;
        internal event EventHandler TargetChanged;
        private bool updatingTarget;
        private Guid? lastTargetId;
        private Control previewContent;

        public BatchWorkspace(Button previewButton, CheckBox bestPractice)
        {
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9F);
            Tabs.TabPages.AddRange(new[] { SetupPage, PreviewPage, ResultsPage });
            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.Controls.Add(ConnectionStatus, 0, 0); outer.Controls.Add(Tabs, 0, 1); Controls.Add(outer);
            var setup = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(16) };
            setup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            setup.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            setup.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (var i = 2; i < 7; i++) setup.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var sourceActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            AddButton(sourceActions, "Paste sources...", () => PasteRequested?.Invoke(this, EventArgs.Empty));
            AddButton(sourceActions, "Import CSV/TXT...", () => ImportRequested?.Invoke(this, EventArgs.Empty));
            AddButton(sourceActions, "Add manually...", () => BrowseRequested?.Invoke(this, EventArgs.Empty));
            AddButton(sourceActions, "Remove selected", () => RemoveRequested?.Invoke(this, EventArgs.Empty));
            Sources.Columns.Add("Source solution", 350); Sources.Columns.Add("Unique name", 350);
            var sourceGroup = new GroupBox { Text = "Selected sources", Dock = DockStyle.Fill, Padding = new Padding(8), MinimumSize = new Size(0, 140) };
            sourceGroup.Controls.Add(Sources);
            var targetGroup = new GroupBox { Text = "Target release solution (unmanaged)", Dock = DockStyle.Fill, Height = 72, Padding = new Padding(8, 22, 8, 8) };
            targetGroup.Controls.Add(Target);
            bestPractice.Dock = DockStyle.Top; bestPractice.Location = Point.Empty;
            AdvancedPanel.Controls.Add(bestPractice);
            AdvancedToggle.CheckedChanged += (s, e) => AdvancedPanel.Visible = AdvancedToggle.Checked;
            previewButton.Text = "Preview components"; previewButton.AutoSize = true; previewButton.Anchor = AnchorStyles.Right;
            setup.Controls.Add(sourceActions, 0, 0); setup.Controls.Add(sourceGroup, 0, 1); setup.Controls.Add(ValidationStatus, 0, 2);
            setup.Controls.Add(targetGroup, 0, 3); setup.Controls.Add(AdvancedToggle, 0, 4); setup.Controls.Add(AdvancedPanel, 0, 5); setup.Controls.Add(previewButton, 0, 6);
            SetupPage.Controls.Add(setup);
            PreviewHost.Controls.Add(PreviewEmpty); PreviewPage.Controls.Add(PreviewHost);
            ResultsPage.Controls.Add(ResultsSummary);
            Target.SelectedIndexChanged += (s, e) => NotifyTargetChanged();
            Target.TextChanged += (s, e) => NotifyTargetChanged();
            Target.SelectionChangeCommitted += (s, e) => NotifyTargetChanged();
        }

        private void NotifyTargetChanged()
        {
            if (updatingTarget) return;
            var id = SelectedTarget?.Id;
            if (id == lastTargetId) return;
            lastTargetId = id;
            TargetChanged?.Invoke(this, EventArgs.Empty);
        }

        internal Entity SelectedTarget => Target.SelectedItem is TargetChoice choice && Target.Text == choice.ToString() ? choice.Entity : null;

        internal void SetTargets(IEnumerable<Entity> solutions)
        {
            updatingTarget = true;
            try
            {
                Target.Items.Clear(); Target.Text = "";
                Target.Items.AddRange(solutions.Where(s => !s.GetAttributeValue<bool>("ismanaged"))
                    .OrderBy(s => s.GetAttributeValue<string>("friendlyname"))
                    .Select(s => new TargetChoice(s)).ToArray());
                Target.SelectedIndex = -1; lastTargetId = null;
            }
            finally { updatingTarget = false; }
        }

        internal void SetSources(IEnumerable<Entity> selected)
        {
            var ids = new HashSet<Guid>(Sources.SelectedItems.Cast<ListViewItem>().Select(i => ((Entity)i.Tag).Id));
            Sources.BeginUpdate();
            try
            {
                Sources.Items.Clear();
                foreach (var entity in selected)
                {
                    var item = new ListViewItem(new[] { entity.GetAttributeValue<string>("friendlyname"), entity.GetAttributeValue<string>("uniquename") }) { Tag = entity };
                    Sources.Items.Add(item); item.Selected = ids.Contains(entity.Id);
                }
            }
            finally { Sources.EndUpdate(); }
        }

        internal void SetPreview(Control content)
        {
            if (previewContent != null)
            {
                PreviewHost.Controls.Remove(previewContent); previewContent.Dispose(); previewContent = null;
            }
            PreviewEmpty.Visible = content == null;
            if (content == null) return;
            previewContent = content; content.Dock = DockStyle.Fill;
            PreviewHost.Controls.Add(content); content.BringToFront(); content.Show();
        }

        private static void AddButton(FlowLayoutPanel panel, string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (s, e) => click(); panel.Controls.Add(button);
        }

        private sealed class TargetChoice
        {
            internal readonly Entity Entity;
            internal TargetChoice(Entity entity) { Entity = entity; }
            public override string ToString() => Entity.GetAttributeValue<string>("friendlyname") + " (" + Entity.GetAttributeValue<string>("uniquename") + ")";
        }
    }
}
