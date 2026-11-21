#if NET48
using Microsoft.Xrm.Sdk;
using MsCrmTools.SolutionComponentsMover.Forms;
using MsCrmTools.SolutionComponentsMover.UserControls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal static class SourceSelectionTests
{
    public static void Run(Action<bool, string> check)
    {
        var imported = WorkspaceTests.Solution("Imported source");
        var manual = WorkspaceTests.Solution("Manual source");
        var all = new[] { imported, manual };
        using (var host = new Form())
        using (var original = new SolutionPicker { CanDisplayManagedSolutions = true, Dock = DockStyle.Fill })
        {
            host.Controls.Add(original); host.Show();
            original.LoadSolutions(all); original.SelectSolutionsByIds(new[] { imported.Id });
            using (var dialog = new SourceSelectionForm(all, original.SelectedSolutions.Select(s => s.Id)))
            {
                dialog.Show();
                var list = (ListView)typeof(SolutionPicker).GetField("lvSolutions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dialog.Picker);
                // Reproduce a normal single click: Windows deselects the old highlighted rows.
                foreach (ListViewItem row in list.Items) row.Selected = false;
                foreach (ListViewItem row in list.Items) if (((Entity)row.Tag).Id == manual.Id) row.Selected = true;
                check(dialog.Picker.SelectedSolutions.Count == 1 && dialog.Picker.SelectedSolutions[0].Id == manual.Id, "single manual choice replaces only the dialog highlight");
                check(original.SelectedSolutions.Count == 1 && original.SelectedSolutions[0].Id == imported.Id, "browsing cannot alter imported source selection");
                check(new HashSet<Guid>(dialog.SelectedSourceIds).SetEquals(new[] { imported.Id, manual.Id }), "manual selection is unioned with imported sources");
                original.SelectSolutionsByIds(dialog.SelectedSourceIds);
                check(original.SelectedSolutions.Count == 2, "committing manual addition retains imported source");
                check(((Button)dialog.AcceptButton).Text == "Add selected sources" && ((Button)dialog.CancelButton).DialogResult == DialogResult.Cancel, "manual dialog has explicit commit and cancellation");
                dialog.Close();
            }
            using (var cancelled = new SourceSelectionForm(all, original.SelectedSolutions.Select(s => s.Id)))
            {
                cancelled.Show(); cancelled.Picker.ClearSelection(); cancelled.Close();
            }
            check(original.SelectedSolutions.Count == 2, "cancelled browsing leaves actual selection unchanged");
            using (var repeated = new SourceSelectionForm(all, original.SelectedSolutions.Select(s => s.Id)))
            {
                check(repeated.SelectedSourceIds.Count == 2, "repeated manual addition does not duplicate sources");
            }
            host.Close();
        }
    }
}
#endif
