#if NET48
using Microsoft.Xrm.Sdk;
using MsCrmTools.SolutionComponentsMover.UserControls;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

internal static class PickerTests
{
    public static void Run(Action<bool, string> check)
    {
        using (var form = new Form())
        using (var picker = new SolutionPicker { CanDisplayManagedSolutions = true, Dock = DockStyle.Fill })
        {
            form.Controls.Add(picker);
            form.Show();
            var first = Solution("First");
            var second = Solution("Second");
            picker.LoadSolutions(new[] { first, second });
            var list = (ListView)typeof(SolutionPicker).GetField("lvSolutions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(picker);
            var filter = (TextBox)typeof(SolutionPicker).GetField("txtFilter", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(picker);
            foreach (ListViewItem item in list.Items) if (((Entity)item.Tag).Id == first.Id) item.Selected = true;
            check(picker.SelectedSolutions.Count == 1 && picker.SelectedSolutions[0].Id == first.Id, "manual source selection");
            filter.Text = "no matches";
            check(list.Items.Count == 1 && picker.SelectedSolutions.Count == 1, "filter keeps manual selection visible");
            picker.SelectSolutionsByIds(new[] { second.Id, second.Id });
            check(picker.SelectedSolutions.Count == 2 && filter.Text.Length == 0, "bulk selection preserves manual source and clears filter");
            filter.Text = "no matches";
            check(list.Items.Count == 2 && picker.SelectedSolutions.Count == 2, "imported sources cannot disappear under filter");
            foreach (ListViewItem item in list.Items) if (((Entity)item.Tag).Id == first.Id) item.Selected = false;
            check(picker.SelectedSolutions.Count == 1 && picker.SelectedSolutions[0].Id == second.Id, "manual deselection after import");
            picker.ClearSelection();
            check(picker.SelectedSolutions.Count == 0, "clear selection");
            filter.Clear();
            picker.MultiSelect = false;
            list.Items[0].Selected = true;
            list.Items[1].Selected = true;
            check(picker.SelectedSolutions.Count == 1, "one target selection");
            picker.LoadSolutions(new Entity[0]);
            check(picker.SelectedSolutions.Count == 0, "connection reload clears old selections");
            form.Close();
        }
    }

    private static Entity Solution(string name)
    {
        var entity = new Entity("solution", Guid.NewGuid());
        entity["friendlyname"] = name;
        entity["uniquename"] = name;
        entity["publisherid"] = new EntityReference("publisher", Guid.NewGuid()) { Name = "Test publisher" };
        entity["installedon"] = DateTime.Today;
        entity["version"] = "1.0.0.0";
        entity["ismanaged"] = false;
        return entity;
    }
}
#endif
