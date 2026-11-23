using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace BluePointLilac.Controls
{
    public sealed class UAWebClient : IDisposable
    {
        private readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public UAWebClient()
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Rmemu/3.3.3.1");
        }

        public byte[] DownloadData(string url) => client.GetByteArrayAsync(url).GetAwaiter().GetResult();

        public string GetWebString(string url)
        {
            try
            {
                string text = client.GetStringAsync(url).GetAwaiter().GetResult();
                return text.ReplaceLineEndings(Environment.NewLine);
            }
            catch(HttpRequestException) { return null; }
            catch(System.Threading.Tasks.TaskCanceledException) { return null; }
        }

        public bool WebStringToFile(string filePath, string fileUrl)
        {
            string contents = GetWebString(fileUrl);
            if(contents == null) return false;
            return WriteDownloadedText(filePath, contents);
        }

        internal static bool WriteDownloadedText(string filePath, string contents, string expectedRoot = null)
        {
            if(string.Equals(Path.GetExtension(filePath), ".xml", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using(var text = new StringReader(contents))
                    using(var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    {
                        var document = new XmlDocument { XmlResolver = null };
                        document.Load(reader);
                        if(expectedRoot != null && document.DocumentElement?.Name != expectedRoot) return false;
                    }
                }
                catch(XmlException) { return false; }
            }
            string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, contents, Encoding.Unicode);
                File.Move(temporaryPath, filePath, true);
                return true;
            }
            finally
            {
                if(File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        public XmlDocument GetWebJsonToXml(string url)
        {
            try
            {
                byte[] bytes = DownloadData(url);
                using(XmlReader reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max))
                {
                    var document = new XmlDocument { XmlResolver = null };
                    document.Load(reader);
                    return document;
                }
            }
            catch(HttpRequestException) { return null; }
            catch(System.Threading.Tasks.TaskCanceledException) { return null; }
            catch(XmlException) { return null; }
        }

        public void Dispose() => client.Dispose();
    }
}
