using MsCrmTools.SolutionComponentsMover.AppCode;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MsCrmTools.SolutionComponentsMover.Forms
{
    internal sealed class BatchPreviewForm : Form
    {
        internal DataGridView Grid { get; }
        internal TextBox Search { get; }
        internal CheckBox Technical { get; }
        internal ComboBox ActionFilter { get; }
        internal Label VisibleCount { get; }

        public BatchPreviewForm(BatchPlan plan, bool embedded = false)
        {
            Text = "Component preview — read only";
            Width = 1150; Height = 720; MinimumSize = embedded ? Size.Empty : new Size(800, 500);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9F);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var add = plan.Components.Count(c => !c.AlreadyExists && !c.TargetPresent);
            var expand = plan.Components.Count(c => !c.AlreadyExists && c.TargetPresent);
            var summary = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 12), Text =
                "Target: " + plan.Target.GetAttributeValue<string>("friendlyname") + " (" + plan.Target.GetAttributeValue<string>("uniquename") + ")" + Environment.NewLine +
                "Sources (" + plan.Settings.SourceSolutions.Count + "): " + string.Join(", ", plan.Settings.SourceSolutions.Select(s => s.GetAttributeValue<string>("friendlyname"))) + Environment.NewLine +
                $"{plan.Components.Count} unique components  |  Add: {add}  |  Expand coverage: {expand}  |  Skip existing: {plan.ExistingCount}  |  Duplicate source entries combined: {plan.RetrievedCount - plan.Components.Count}" };
            var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            toolbar.Controls.Add(new Label { Text = "Search:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            Search = new TextBox { Width = 300, AccessibleName = "Search preview components" };
            Technical = new CheckBox { Text = "Show technical details", AutoSize = true, Padding = new Padding(12, 2, 0, 0) };
            ActionFilter = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Filter planned actions" };
            ActionFilter.Items.AddRange(new object[] { "All actions", "To add", "Existing", "Expand" });
            ActionFilter.SelectedIndex = 0;
            VisibleCount = new Label { AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
            toolbar.Controls.Add(Search); toolbar.Controls.Add(ActionFilter); toolbar.Controls.Add(Technical); toolbar.Controls.Add(VisibleCount);
            Grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToOrderColumns = true, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
            };
            AddColumn("Component", "Component name", 35);
            AddColumn("Type", "Type", 15);
            AddColumn("Sources", "Source solution(s)", 25);
            AddColumn("Action", "Planned action", 25);
            AddColumn("Id", "Component ID", 30);
            AddColumn("Behavior", "Inclusion", 20);
            Grid.Columns["Id"].Visible = Grid.Columns["Behavior"].Visible = false;
            Grid.Columns["Sources"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            foreach (var component in plan.Components.OrderBy(c => c.TypeName).ThenBy(c => c.DisplayName ?? c.Id.ToString()))
            {
                var index = Grid.Rows.Add(component.DisplayName ?? "Name unavailable (see technical details)",
                    component.TypeName ?? "Component type " + component.Type, string.Join("; ", component.Sources),
                    component.ActionLabel, component.Id.ToString(), Inclusion(component.Behavior));
                var row = Grid.Rows[index];
                row.Tag = component;
                row.DefaultCellStyle.BackColor = component.AlreadyExists ? Color.WhiteSmoke : component.TargetPresent ? Color.AliceBlue : Color.Honeydew;
                row.Cells["Component"].ToolTipText = component.NameWarning ?? component.DisplayName ?? "Name lookup is not available for this component type.";
            }
            Search.TextChanged += (s, e) => ApplyFilters();
            ActionFilter.SelectedIndexChanged += (s, e) => ApplyFilters();
            ApplyFilters();
            Technical.CheckedChanged += (s, e) => Grid.Columns["Id"].Visible = Grid.Columns["Behavior"].Visible = Technical.Checked;
            var notes = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Height = 75, ScrollBars = ScrollBars.Vertical,
                Text = "Preview only — no CRM changes have been made. Sources will remain unchanged. Filters affect this view only; copying uses the full reviewed plan." + Environment.NewLine + string.Join(Environment.NewLine, plan.Warnings) };
            var close = new Button { Text = "Close preview", AutoSize = true, Anchor = AnchorStyles.Right, DialogResult = DialogResult.OK };
            close.Visible = !embedded;
            if (!embedded) { AcceptButton = close; CancelButton = close; }
            layout.Controls.Add(summary, 0, 0); layout.Controls.Add(toolbar, 0, 1); layout.Controls.Add(Grid, 0, 2);
            layout.Controls.Add(notes, 0, 3); layout.Controls.Add(close, 0, 4);
            Controls.Add(layout);
        }

        private void ApplyFilters()
        {
            Grid.CurrentCell = null;
            var visible = 0;
            foreach (DataGridViewRow row in Grid.Rows)
            {
                var component = (PlannedComponent)row.Tag;
                var actionMatches = ActionFilter.SelectedIndex == 0 ||
                    (ActionFilter.SelectedIndex == 1 && !component.AlreadyExists && !component.TargetPresent) ||
                    (ActionFilter.SelectedIndex == 2 && component.AlreadyExists) ||
                    (ActionFilter.SelectedIndex == 3 && !component.AlreadyExists && component.TargetPresent);
                row.Visible = actionMatches && row.Cells.Cast<DataGridViewCell>().Any(c =>
                    Convert.ToString(c.Value).IndexOf(Search.Text, StringComparison.OrdinalIgnoreCase) >= 0);
                if (row.Visible) visible++;
            }
            VisibleCount.Text = $"Showing {visible} of {Grid.Rows.Count}";
        }

        private void AddColumn(string name, string title, int weight)
        {
            Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, FillWeight = weight, MinimumWidth = 90, SortMode = DataGridViewColumnSortMode.Automatic });
        }

        private static string Inclusion(int? behavior)
        {
            return behavior == 0 ? "Include subcomponents (0)" : behavior == 1 ? "Exclude subcomponents (1)" : behavior == 2 ? "Shell only (2)" : "Not specified";
        }
    }
}
