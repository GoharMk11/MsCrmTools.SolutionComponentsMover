using Microsoft.Xrm.Sdk;
using MsCrmTools.SolutionComponentsMover.UserControls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Label = System.Windows.Forms.Label;

namespace MsCrmTools.SolutionComponentsMover.Forms
{
    internal sealed class SourceSelectionForm : Form
    {
        private readonly HashSet<Guid> existing;
        internal SolutionPicker Picker { get; }
        internal List<Guid> SelectedSourceIds => existing.Union(Picker.SelectedSolutions.Select(s => s.Id)).ToList();

        public SourceSelectionForm(IEnumerable<Entity> solutions, IEnumerable<Guid> selected)
        {
            existing = new HashSet<Guid>(selected);
            Text = "Add source solutions";
            Width = 1000; Height = 620; StartPosition = FormStartPosition.CenterParent;
            Picker = new SolutionPicker { CanDisplayManagedSolutions = true, MultiSelect = true, Dock = DockStyle.Fill };
            Picker.LoadSolutions(solutions);
            Picker.SelectSolutionsByIds(existing);
            var note = new Label { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8),
                Text = "Select additional sources. Existing sources are kept. Use Remove selected on Setup to remove sources." };
            var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var add = new Button { AutoSize = true, Text = "Add selected sources", DialogResult = DialogResult.OK };
            var cancel = new Button { AutoSize = true, Text = "Cancel", DialogResult = DialogResult.Cancel };
            footer.Controls.Add(add); footer.Controls.Add(cancel);
            AcceptButton = add; CancelButton = cancel;
            Controls.Add(Picker); Controls.Add(footer); Controls.Add(note);
        }
    }
}
