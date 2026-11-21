using MsCrmTools.SolutionComponentsMover.AppCode;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MsCrmTools.SolutionComponentsMover.Forms
{
    internal sealed class RunComparisonForm : Form
    {
        internal DataGridView Grid { get; }
        public RunComparisonForm(RunReport previous, RunReport current)
        {
            var differences = PlanComparison.Compare(previous, current);
            Text = "Compare selected plans — read only"; Width = 1050; Height = 650; StartPosition = FormStartPosition.CenterParent;
            var heading = new Label { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10), Text =
                "Previous: " + previous.StartedUtc + " | " + previous.Status + " | Build " + previous.Build + "\r\n" +
                "Current target: " + current.Target.Name + " | " + differences.Count + " difference(s)\r\n" +
                "This compares plan membership, inclusion and available fingerprints. Removed from plan does NOT remove anything from CRM." };
            Grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            foreach (var title in new[] { "Change", "Component name", "Type", "Previous inclusion", "Current inclusion", "Component ID" }) Grid.Columns.Add(title, title);
            foreach (var difference in differences)
            {
                var c = difference.After ?? difference.Before;
                Grid.Rows.Add(difference.Change, c.Name ?? "Name unavailable", c.TypeName ?? c.Type.ToString(),
                    difference.Before?.Behavior?.ToString() ?? "—", difference.After?.Behavior?.ToString() ?? "—", c.Id);
            }
            Controls.Add(Grid); Controls.Add(heading);
        }
    }
}
