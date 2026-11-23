using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Methods;
using System;
using System.Linq;
using System.IO;
using System.Windows.Forms;

namespace Rmemu.Controls.Interfaces
{
    interface ITsiDeleteItem
    {
        DeleteMeMenuItem TsiDeleteMe { get; set; }
        void DeleteMe();
    }

    interface ITsiRegDeleteItem : ITsiDeleteItem
    {
        string Text { get; }
        string RegPath { get; }
        string[] ExtraBackupPaths => Array.Empty<string>();
    }

    sealed class DeleteMeMenuItem : ToolStripMenuItem
    {
        public DeleteMeMenuItem(ITsiDeleteItem item) : base(AppString.Menu.Delete)
        {
            this.Click += (sender, e) =>
            {
                if(item is ITsiRegDeleteItem regItem && AppConfig.AutoBackup)
                {
                    if(AppMessageBox.Show(AppString.Message.DeleteButCanRestore,
                     MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    try
                    {
                        string date = DateTime.Today.ToString("yyyy-MM-dd");
                        string time = DateTime.Now.ToString("HH.mm.ss.fffffff");
                        string directory = Path.Combine(AppConfig.BackupDir, date);
                        Directory.CreateDirectory(directory);
                        string[] paths = new[] { regItem.RegPath }.Concat(regItem.ExtraBackupPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        for(int i = 0; i < paths.Length; i++)
                        {
                            string suffix = paths.Length == 1 ? "" : " - " + (i + 1).ToString();
                            string fileName = ObjectPath.RemoveIllegalChars(regItem.Text) + suffix + " - " + time + ".reg";
                            ExternalProgram.ExportRegistry(paths[i], Path.Combine(directory, fileName));
                        }
                    }
                    catch(Exception ex)
                    {
                        AppMessageBox.Show(AppString.W("BackupFailedNotDeleted") + ex.Message);
                        return;
                    }
                }
                else if(AppMessageBox.Show(AppString.Message.ConfirmDeletePermanently,
                     MessageBoxButtons.YesNo) != DialogResult.Yes) return;

                MyListItem listItem = (MyListItem)item;
                MyList list = (MyList)listItem.Parent;
                int index = list.GetItemIndex(listItem);
                try
                {
                    item.DeleteMe();
                }
                catch
                {
                    AppMessageBox.Show(AppString.Message.AuthorityProtection);
                    return;
                }
                list.Controls.Remove(listItem);
                if(list.Controls.Count != 0) list.Controls[Math.Min(index, list.Controls.Count - 1)].Focus();
                else (list.Owner ?? (Control)list).Focus();
                listItem.Dispose();
            };
        }
    }
}