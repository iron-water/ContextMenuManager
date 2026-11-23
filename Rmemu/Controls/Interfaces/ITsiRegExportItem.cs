using BluePointLilac.Methods;
using Rmemu.Methods;
using System;
using System.IO;
using System.Windows.Forms;

namespace Rmemu.Controls.Interfaces
{
    interface ITsiRegExportItem
    {
        string Text { get; set; }
        string RegPath { get; }
        ContextMenuStrip ContextMenuStrip { get; set; }
        RegExportMenuItem TsiRegExport { get; set; }
    }

    sealed class RegExportMenuItem : ToolStripMenuItem
    {
        public RegExportMenuItem(ITsiRegExportItem item) : base(AppString.Menu.ExportRegistry)
        {
            item.ContextMenuStrip.Opening += (sender, e) =>
            {
                using(var key = RegistryEx.GetRegistryKey(item.RegPath))
                    this.Visible = key != null;
            };
            this.Click += (sender, e) =>
            {
                try
                {
                    using(SaveFileDialog dlg = new SaveFileDialog())
                    {
                        string date = DateTime.Today.ToString("yyyy-MM-dd");
                        string time = DateTime.Now.ToString("HH.mm.ss.fffffff");
                        string dirPath = Path.Combine(AppConfig.BackupDir, date);
                        string fileName = ObjectPath.RemoveIllegalChars(item.Text) + " - " + time + ".reg";
                        Directory.CreateDirectory(dirPath);
                        dlg.FileName = fileName;
                        dlg.InitialDirectory = dirPath;
                        dlg.Filter = $"{AppString.Dialog.RegistryFile}|*.reg";
                        if(dlg.ShowDialog() == DialogResult.OK)
                            ExternalProgram.ExportRegistry(item.RegPath, dlg.FileName);
                    }
                }
                catch(Exception ex)
                {
                    AppMessageBox.Show(AppString.W("ExportFailed") + ex.Message);
                }
            };
        }
    }
}