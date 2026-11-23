using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Methods;
using System;
using System.Windows.Forms;

namespace Rmemu
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            if(!SingleInstance.WaitForPreviousInstance(Environment.GetCommandLineArgs())) return;
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            FluentTheme.Initialize();
            if(SingleInstance.IsRunning()) return;
            AppString.LoadStrings();
            AppString.General.AppName = "Rmemu";
            Updater.PeriodicUpdate();
            XmlDicHelper.ReloadDics();
            Application.Run(new MainForm());
        }
    }
}