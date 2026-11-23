using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Controls;
using Rmemu.Methods;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Rmemu
{
    sealed partial class MainForm : MyMainForm
    {
        public MainForm()
        {
            this.TopMost = AppConfig.TopMost;
            this.Size = AppConfig.MainFormSize;
            this.Text = "Rmemu";
            this.Controls.Add(explorerRestarter);
            MainBody.Controls.AddRange(MainControls);
            this.Shown += (sender, e) => FirstRunDownloadLanguage();
            this.FormClosing += (sender, e) => CloseMainForm();
            DragDropToAnalysis();
            InitializeWorkspace();
        }


        private Control[] MainControls => new Control[]
        {
            shellList, shellNewList, sendToList, openWithList, winXList,
            enhanceMenusList, detailedEditList, guidBlockedList, iEList,
            appSettingBox, languagesBox, dictionariesBox, aboutMeBox, donateBox
        };

        readonly ShellList shellList = new ShellList();
        readonly ShellNewList shellNewList = new ShellNewList();
        readonly SendToList sendToList = new SendToList();
        readonly OpenWithList openWithList = new OpenWithList();
        readonly WinXList winXList = new WinXList();

        readonly EnhanceMenusList enhanceMenusList = new EnhanceMenusList();
        readonly DetailedEditList detailedEditList = new DetailedEditList();
        readonly GuidBlockedList guidBlockedList = new GuidBlockedList();
        readonly IEList iEList = new IEList();

        readonly AppSettingBox appSettingBox = new AppSettingBox();
        readonly LanguagesBox languagesBox = new LanguagesBox();
        readonly DictionariesBox dictionariesBox = new DictionariesBox();
        readonly ReadOnlyRichTextBox aboutMeBox = new ReadOnlyRichTextBox();
        readonly DonateBox donateBox = new DonateBox();
        readonly ExplorerRestarter explorerRestarter = new ExplorerRestarter();

        static readonly string[] GeneralItems =
        {
            AppString.SideBar.File,
            AppString.SideBar.Folder,
            AppString.SideBar.Directory,
            AppString.SideBar.Background,
            AppString.SideBar.Desktop,
            AppString.SideBar.Drive,
            AppString.SideBar.AllObjects,
            AppString.SideBar.Computer,
            AppString.SideBar.RecycleBin,
            AppString.SideBar.Library,
            null,
            AppString.SideBar.New,
            AppString.SideBar.SendTo,
            AppString.SideBar.OpenWith,
            null,
            AppString.SideBar.WinX
        };
        static readonly string[] GeneralItemInfos =
        {
            AppString.StatusBar.File,
            AppString.StatusBar.Folder,
            AppString.StatusBar.Directory,
            AppString.StatusBar.Background,
            AppString.StatusBar.Desktop,
            AppString.StatusBar.Drive,
            AppString.StatusBar.AllObjects,
            AppString.StatusBar.Computer,
            AppString.StatusBar.RecycleBin,
            AppString.StatusBar.Library,
            null,
            AppString.StatusBar.New,
            AppString.StatusBar.SendTo,
            AppString.StatusBar.OpenWith,
            null,
            AppString.StatusBar.WinX
        };

        static readonly string[] TypeItems =
        {
            AppString.SideBar.LnkFile,
            AppString.SideBar.UwpLnk,
            AppString.SideBar.ExeFile,
            null,
            AppString.SideBar.CustomExtension,
            AppString.SideBar.PerceivedType,
            AppString.SideBar.DirectoryType,
            null,
            AppString.SideBar.UnknownType,
            AppString.SideBar.MenuAnalysis
        };
        static readonly string[] TypeItemInfos =
        {
            AppString.StatusBar.LnkFile,
            AppString.StatusBar.UwpLnk,
            AppString.StatusBar.ExeFile,
            null,
            AppString.StatusBar.CustomExtension,
            AppString.StatusBar.PerceivedType,
            AppString.StatusBar.DirectoryType,
            null,
            AppString.StatusBar.UnknownType,
            AppString.StatusBar.MenuAnalysis
        };

        static readonly string[] OtherRuleItems =
        {
            AppString.SideBar.EnhanceMenu,
            AppString.SideBar.DetailedEdit,
            null,
            AppString.SideBar.DragDrop,
            AppString.SideBar.PublicReferences,
            AppString.SideBar.CustomRegPath,
            null,
            AppString.SideBar.GuidBlocked,
            AppString.SideBar.IEMenu
        };
        static readonly string[] OtherRuleItemInfos =
        {
            AppString.StatusBar.EnhanceMenu,
            AppString.StatusBar.DetailedEdit,
            null,
            AppString.StatusBar.DragDrop,
            AppString.StatusBar.PublicReferences,
            AppString.StatusBar.CustomRegPath,
            null,
            AppString.StatusBar.GuidBlocked,
            AppString.StatusBar.IEMenu
        };

        static readonly string[] AboutItems =
        {
            AppString.SideBar.AppSetting,
            AppString.SideBar.AppLanguage,
            AppString.SideBar.Dictionaries,
            AppString.SideBar.AboutApp,
            AppString.SideBar.Donate
        };


        static readonly ShellList.Scenes[] GeneralShellScenes =
        {
            ShellList.Scenes.File,
            ShellList.Scenes.Folder,
            ShellList.Scenes.Directory,
            ShellList.Scenes.Background,
            ShellList.Scenes.Desktop,
            ShellList.Scenes.Drive,
            ShellList.Scenes.AllObjects,
            ShellList.Scenes.Computer,
            ShellList.Scenes.RecycleBin,
            ShellList.Scenes.Library
        };

        static readonly ShellList.Scenes?[] TypeShellScenes =
        {
            ShellList.Scenes.LnkFile,
            ShellList.Scenes.UwpLnk,
            ShellList.Scenes.ExeFile,
            null,
            ShellList.Scenes.CustomExtension,
            ShellList.Scenes.PerceivedType,
            ShellList.Scenes.DirectoryType,
            null,
            ShellList.Scenes.UnknownType,
            ShellList.Scenes.MenuAnalysis
        };

        internal int SelectedCategory { get; private set; }
        internal int SelectedLocation { get; private set; }

        public void JumpItem(int category, int location) => Navigate(category, location);



        private void SwitchItem()
        {
            foreach(Control ctr in MainControls)
            {
                ctr.Visible = false;
                if(ctr is MyList list) list.ClearItems();
            }
            if(SelectedLocation == -1) return;
            switch(SelectedCategory)
            {
                case 0:
                    SwitchGeneralItem(); break;
                case 1:
                    SwitchTypeItem(); break;
                case 2:
                    SwitchOtherRuleItem(); break;
                case 4:
                    SwitchAboutItem(); break;
            }
            this.SuspendMainBodyWhenMove = false;
            WorkspaceChanged();
        }


        private void DragDropToAnalysis()
        {
            var droper = new ElevatedFileDroper(this);
            droper.DragDrop += (sender, e) =>
            {
                ShellList.CurrentFileObjectPath = droper.DropFilePaths[0];
                JumpItem(1, 9);
            };
        }

        private void SwitchGeneralItem()
        {
            switch(SelectedLocation)
            {
                case 11:
                    shellNewList.LoadItems(); shellNewList.Visible = true; break;
                case 12:
                    sendToList.LoadItems(); sendToList.Visible = true; break;
                case 13:
                    openWithList.LoadItems(); openWithList.Visible = true; break;
                case 15:
                    winXList.LoadItems(); winXList.Visible = true; break;
                default:
                    shellList.Scene = GeneralShellScenes[SelectedLocation];
                    shellList.LoadItems(); shellList.Visible = true; break;
            }
        }

        private void SwitchTypeItem()
        {
            shellList.Scene = (ShellList.Scenes)TypeShellScenes[SelectedLocation];
            shellList.LoadItems();
            shellList.Visible = true;
        }

        private void SwitchOtherRuleItem()
        {
            switch(SelectedLocation)
            {
                case 0:
                    enhanceMenusList.ScenePath = null; enhanceMenusList.LoadItems(); enhanceMenusList.Visible = true; break;
                case 1:
                    detailedEditList.GroupGuid = Guid.Empty; detailedEditList.LoadItems(); detailedEditList.Visible = true; break;
                case 3:
                    shellList.Scene = ShellList.Scenes.DragDrop; shellList.LoadItems(); shellList.Visible = true; break;
                case 4:
                    shellList.Scene = ShellList.Scenes.CommandStore; shellList.LoadItems(); shellList.Visible = true; break;
                case 5:
                    shellList.Scene = ShellList.Scenes.CustomRegPath; shellList.LoadItems(); shellList.Visible = true; break;
                case 7:
                    guidBlockedList.LoadItems(); guidBlockedList.Visible = true; break;
                case 8:
                    iEList.LoadItems(); iEList.Visible = true; break;
            }
        }

        private void SwitchAboutItem()
        {
            switch(SelectedLocation)
            {
                case 0:
                    appSettingBox.LoadItems(); appSettingBox.Visible = true;
                    break;
                case 1:
                    languagesBox.LoadLanguages(); languagesBox.Visible = true;
                    break;
                case 2:
                    dictionariesBox.LoadText(); dictionariesBox.Visible = true;
                    break;
                case 3:
                    if(aboutMeBox.TextLength == 0) aboutMeBox.Text = AppString.W("AboutText");
                    aboutMeBox.Visible = true; break;
                case 4:
                    donateBox.Visible = true; break;
            }
        }


        private void FirstRunDownloadLanguage()
        {
            if(AppConfig.IsFirstRun && CultureInfo.CurrentUICulture.Name != "zh-CN")
            {
                if(AppMessageBox.Show("It is detected that you may be running this program for the first time,\n" +
                    "and your system display language is not simplified Chinese (zh-CN),\n" +
                    "do you need to download another language?",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    JumpItem(4, 1);
                    languagesBox.ShowLanguageDialog();
                }
            }
        }

        private void CloseMainForm()
        {
            if(explorerRestarter.Visible && AppMessageBox.Show(AppString.W("CloseRestartConfirm"), MessageBoxButtons.OKCancel) == DialogResult.OK)
                ExternalProgram.RestartExplorer();
            this.Opacity = 0;
            this.WindowState = FormWindowState.Normal;
            explorerRestarter.Visible = false;
            AppConfig.MainFormSize = this.Size;
        }
    }
}