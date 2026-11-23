using BluePointLilac.Methods;
using Rmemu.Methods;
using System.Windows.Forms;

namespace Rmemu.Controls.Interfaces
{
    interface ITsiWebSearchItem
    {
        string SearchText { get; }
        WebSearchMenuItem TsiSearch { get; set; }
    }

    sealed class WebSearchMenuItem : ToolStripMenuItem
    {
        public WebSearchMenuItem(ITsiWebSearchItem item) : base(AppString.Menu.WebSearch)
        {
            this.Click += (sender, e) =>
            {
                string url = AppConfig.EngineUrl.Replace("%s", item.SearchText);
                ExternalProgram.OpenWebUrl(url);
            };
        }
    }
}