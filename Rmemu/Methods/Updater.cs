using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Methods;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace Rmemu.Methods
{
    sealed class Updater
    {
        private static int updating;
        internal static string LastError { get; private set; }

        internal static bool IsUpdateDue(DateTime lastCheck, int frequency, DateTime today)
            => frequency >= 0 && (today.Date - lastCheck.Date).TotalDays >= frequency;

        /// <summary>定期检查更新</summary>
        public static void PeriodicUpdate()
        {
            int day = AppConfig.UpdateFrequency;
            if(IsUpdateDue(AppConfig.LastCheckUpdateTime, day, DateTime.Today)) Task.Run(() => Update(false));
        }

        /// <summary>更新字典；手动操作时查看原项目的上游发布</summary>
        /// <param name="isManual">是否为手动点击更新</param>
        public static void Update(bool isManual)
        {
            if(Interlocked.CompareExchange(ref updating, 1, 0) != 0)
            {
                if(isManual) AppMessageBox.Show(AppString.W("UpdateBusy"));
                return;
            }
            try
            {
                LastError = null;
                UpdateText(isManual);
                AppConfig.LastCheckUpdateTime = DateTime.Today;
                if(isManual) CheckUpstreamRelease();
            }
            catch(Exception ex)
            {
                LastError = ex.Message;
                if(isManual) AppMessageBox.Show(AppString.W("DictUpdateFailed") + ex.Message);
            }
            finally { Volatile.Write(ref updating, 0); }
        }

        /// <summary>只查看原项目发布信息，永不下载或替换此分支的可执行文件。</summary>
        private static void CheckUpstreamRelease()
        {
            string releaseUrl = AppConfig.RequestUseGithub ? AppConfig.GithubLatest : AppConfig.GiteeReleases;
            try
            {
                using(UAWebClient client = new UAWebClient())
                {
                    string apiUrl = AppConfig.RequestUseGithub ? AppConfig.GithubLatestApi : AppConfig.GiteeLatestApi;
                    XmlDocument document = client.GetWebJsonToXml(apiUrl);
                    XmlNode root = document?.DocumentElement;
                    string version = root?.SelectSingleNode("tag_name")?.InnerText;
                    if(string.IsNullOrWhiteSpace(version)) throw new InvalidDataException(AppString.W("UpstreamVersionUnreadable"));
                    string notes = root.SelectSingleNode("body")?.InnerText;
                    string message = AppString.W("UpstreamLatest", version)
                        + AppString.W("UpstreamNote");
                    if(!string.IsNullOrWhiteSpace(notes)) message += "\r\n\r\n" + MachinedInfo(notes);
                    message += AppString.W("UpstreamOpenPage");
                    if(AppMessageBox.Show(message, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        ExternalProgram.OpenWebUrl(releaseUrl);
                }
            }
            catch(Exception ex)
            {
                string message = AppString.W("UpstreamReadFailed") + ex.Message
                    + AppString.W("UpstreamOpenPage2");
                if(AppMessageBox.Show(message, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    ExternalProgram.OpenWebUrl(releaseUrl);
            }
        }

        /// <summary>更新程序字典</summary>
        /// <param name="isManual">是否为手动点击更新</param>
        private static void UpdateText(bool isManual)
        {
            string dirUrl;
            string[] filePaths;
            void WriteFiles(string dirName, out string succeeded, out string failed)
            {
                succeeded = failed = "";
                foreach(string filePath in filePaths)
                {
                    bool flag;
                    try
                    {
                        using(UAWebClient client = new UAWebClient())
                        {
                            string contents = client.GetWebString($"{dirUrl}/{Path.GetFileName(filePath)}");
                            string expectedRoot = filePath.Equals(AppConfig.WebUwpModeItemsDic, StringComparison.OrdinalIgnoreCase) ? "Scene"
                                : Path.GetExtension(filePath).Equals(".xml", StringComparison.OrdinalIgnoreCase) ? "Data" : null;
                            flag = contents != null && UAWebClient.WriteDownloadedText(filePath, contents, expectedRoot);
                        }
                    }
                    catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                    {
                        flag = false;
                        LastError = ex.Message;
                    }
                    string item = "\r\n ● " + Path.GetFileName(filePath);
                    if(flag) succeeded += item;
                    else failed += item;
                }
                dirName = "\r\n\r\n" + dirName + ":";
                if(succeeded != "") succeeded = dirName + succeeded;
                if(failed != "") failed = dirName + failed;
            }

            dirUrl = AppConfig.RequestUseGithub ? AppConfig.GithubTexts : AppConfig.GiteeTexts;
            filePaths = new[]
            {
                AppConfig.WebGuidInfosDic, AppConfig.WebEnhanceMenusDic,
                AppConfig.WebDetailedEditDic, AppConfig.WebUwpModeItemsDic
            };
            WriteFiles("Dictionaries", out string succeeded1, out string failed1);

            dirUrl = AppConfig.RequestUseGithub ? AppConfig.GithubLangsRawDir : AppConfig.GiteeLangsRawDir;
            filePaths = Directory.GetFiles(AppConfig.LangsDir, "*.ini");
            WriteFiles("Languages", out string succeeded2, out string failed2);

            if(isManual)
            {
                string failed = failed1 + failed2;
                string succeeded = succeeded1 + succeeded2;
                if(failed != "") AppMessageBox.Show(AppString.Message.WebDataReadFailed + failed);
                if(succeeded != "") AppMessageBox.Show(AppString.Message.DicUpdateSucceeded + succeeded,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>加工处理更新信息，去掉标题头</summary>
        private static string MachinedInfo(string info)
        {
            StringBuilder result = new StringBuilder();
            string[] lines = info.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for(int m = 0; m < lines.Length; m++)
            {
                string line = lines[m];
                for(int n = 1; n <= 6; n++)
                {
                    if(line.StartsWith(new string('#', n) + ' '))
                    {
                        line = line.Substring(n + 1);
                        break;
                    }
                }
                result.AppendLine(line);
            }
            return result.ToString();
        }
    }
}