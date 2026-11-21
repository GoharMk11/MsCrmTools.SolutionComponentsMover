#if NET48
using Microsoft.Xrm.Sdk;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Label = System.Windows.Forms.Label;

internal static class HostUiTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check, string pluginPath)
    {
        var pluginDirectory = Path.GetDirectoryName(Path.GetFullPath(pluginPath));
        ResolveEventHandler resolver = (s, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            foreach (var directory in new[] { pluginDirectory, Path.GetDirectoryName(pluginDirectory) })
                foreach (var extension in new[] { ".dll", ".exe" })
                {
                    var file = Path.Combine(directory, name + extension);
                    if (File.Exists(file)) return Assembly.LoadFrom(file);
                }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            var assembly = Assembly.LoadFrom(pluginPath);
            var type = assembly.GetType("MsCrmTools.SolutionComponentsMover.MainControl", true);
            using (var control = (UserControl)Activator.CreateInstance(type))
            using (var form = new Form { Text = "Batch Solution Copier — actual plugin UI", Width = 1150, Height = 780 })
            {
                control.Dock = DockStyle.Fill; form.Controls.Add(control); form.Show();
                var workspace = type.GetField("workspace", Flags).GetValue(control);
                var workspaceType = workspace.GetType();
                var tabs = (TabControl)workspaceType.GetProperty("Tabs", Flags).GetValue(workspace);
                check(tabs.TabPages.Count == 3 && tabs.SelectedIndex == 0, "actual production plugin constructs on Setup");
                var picker = type.GetField("sourceSolutionPicker", Flags).GetValue(control);
                var sources = new[] { WorkspaceTests.Solution("Source A"), WorkspaceTests.Solution("Source B"), WorkspaceTests.Solution("Source C") };
                picker.GetType().GetMethod("LoadSolutions").Invoke(picker, new object[] { sources });
                picker.GetType().GetMethod("SelectSolutionsByIds").Invoke(picker, new object[] { sources.Select(s => s.Id).ToArray() });
                var list = (ListView)workspaceType.GetProperty("Sources", Flags).GetValue(workspace);
                check(list.Items.Count == 3, "production selection events refresh the selected-source list");
                var target = WorkspaceTests.Solution("Release Test");
                var targetPicker = type.GetField("targetSolutionPicker", Flags).GetValue(control);
                targetPicker.GetType().GetMethod("LoadSolutions").Invoke(targetPicker, new object[] { new[] { target } });
                workspaceType.GetMethod("SetTargets", Flags).Invoke(workspace, new object[] { new[] { target } });
                var dropdown = (ComboBox)workspaceType.GetProperty("Target", Flags).GetValue(workspace);
                dropdown.SelectedIndex = 0;
                var selected = (System.Collections.IList)targetPicker.GetType().GetProperty("SelectedSolutions").GetValue(targetPicker);
                check(selected.Count == 1, "production target dropdown updates the existing single-target picker");
                var connection = (Label)type.GetField("lblConnection", Flags).GetValue(control); connection.Text = "Connected: DEV (sample data, no CRM connection)";
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugin-setup.png"), ImageFormat.Png);
                }
                var input = (TextBox)type.GetField("txtBatchInput", Flags).GetValue(control);
                var sawInput = false;
                form.BeginInvoke(new Action(() =>
                {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "Paste / import source solutions");
                    if (dialog == null) return;
                    try { input.Text = "SourceA" + Environment.NewLine + "SourceB"; sawInput = input.Visible && input.Parent != null; }
                    finally { dialog.Close(); }
                }));
                type.GetMethod("OpenInputDialog", Flags).Invoke(control, new object[] { false });
                check(sawInput && !input.IsDisposed && input.Text.Contains("SourceB"), "production paste dialog preserves input controls after closing");
                check(list.Items.Count == 3, "editing import text preserves manually selected sources");
                type.GetMethod("SetBatchBusy", Flags).Invoke(control, new object[] { true, false });
                check(!tabs.TabPages[0].Enabled, "production Setup locks while background work runs");
                type.GetMethod("SetBatchBusy", Flags).Invoke(control, new object[] { false, false });
                check(tabs.TabPages[0].Enabled, "production Setup unlocks when work finishes");
                form.Close();
            }
        }
        finally { AppDomain.CurrentDomain.AssemblyResolve -= resolver; }
    }
}
#endif
