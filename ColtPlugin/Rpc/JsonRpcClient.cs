using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Services.Protocols;
using System.Windows.Forms;
using System.Xml;
using ColtPlugin.Resources;
using Jayrock.Json;
using PluginCore;
using PluginCore.Managers;
using Timer = System.Timers.Timer;

namespace ColtPlugin.Rpc
{
    public class JsonRpcException : Exception
    {
        public string TypeName;
        public JsonRpcException(string typeName, string message) : base((typeName == null) ? "" : ("[" + typeName + "] ") + message)
        {
            TypeName = typeName;
        }
    }

    /// <summary>
    /// Based on sample client from Jayrock author
    /// http://markmail.org/message/xwlaeb3nfanv2kgm
    /// </summary>
    [DesignerCategory("")]
    public class JsonRpcClient : HttpWebClientProtocol
    {
        int id;
        readonly string projectPath;

        public JsonRpcClient(string projectPath)
        {
            this.projectPath = projectPath;

            string coltFolder = string.Format("{0}\\.colt_as\\", Environment.GetEnvironmentVariable("USERPROFILE"));

            XmlDocument storageDescriptor = new XmlDocument();
            storageDescriptor.Load(string.Format("{0}storage.xml", coltFolder));

            // <xml>
            //  <storage path='/Users/makc/Desktop/Site/site.colt' subDir='8572a4d3' />
            XmlNodeList storageList = storageDescriptor.SelectNodes(string.Format("/xml/storage[@path='{0}']", this.projectPath));
            if (storageList.Count == 1)
            {
                string storage = storageList[0].Attributes["subDir"].Value;
                int port = int.Parse ( File.ReadAllText(string.Format("{0}storage\\{1}\\rpc.info", coltFolder, storage)).Split(':')[1] );

                Url = string.Format("http://127.0.0.1:{0}/rpc/coltService", port); return;
            }

            Url = "http://127.0.0.1:8092/rpc/coltService";
        }

        public void PingOrRunCOLT(string executable)
        {
            try
            {
                Invoke("ping");
            }
            catch (Exception)
            {
                // put it on recent files list
                string coltFolder = string.Format("{0}\\.colt_as\\", Environment.GetEnvironmentVariable("USERPROFILE"));

                XmlDocument workingSet = new XmlDocument();
                workingSet.Load(string.Format("{0}workingset.xml", coltFolder));
                XmlElement root = (XmlElement)workingSet.SelectSingleNode("/workingset");
                XmlElement project = (XmlElement)root.PrependChild(workingSet.CreateElement("", "project", ""));
                project.Attributes.Append(workingSet.CreateAttribute("path")).Value = projectPath;
                workingSet.Save(string.Format("{0}workingset.xml", coltFolder));

                // open COLT exe
                Process.Start(executable);
            }
        }

        public virtual void InvokeAsync(string method, Callback callback, params object[] args)
        {
            JsonRpcClientAsyncHelper helper = new JsonRpcClientAsyncHelper(this, method, callback, args);
        }

        public virtual object Invoke(string method, params object[] args)
        {
            Console.WriteLine("Invoke: method = {0}", method);

            WebRequest request = GetWebRequest(new Uri(Url));
            request.Method = "POST";

            using (Stream stream = request.GetRequestStream())
            using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
            {
                JsonObject call = new JsonObject();
                call["id"] = ++id;
                call["method"] = method;
                call["params"] = args;
                call.Export(new JsonTextWriter(writer));
            }

            using (WebResponse response = GetWebResponse(request))
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                JsonObject answer = new JsonObject();
                answer.Import(new JsonTextReader(reader));
                object errorObject = answer["error"];
                if (errorObject != null) OnError(errorObject);
                return answer["result"];
            }
        }

        protected virtual void OnError(object errorObject)
        {
            JsonObject error = errorObject as JsonObject;
            if (error == null) throw new Exception(errorObject as string);
            string message = error["message"] as string;
            if (message == null) message = "";
            string exceptionType = null;
            JsonObject data = error["data"] as JsonObject;
            if (data != null) exceptionType = data["exceptionTypeName"] as string;
            throw new JsonRpcException(exceptionType, message);
        }
    }

    public delegate void Callback (object result);

    class JsonRpcClientAsyncHelper
    {
        JsonRpcClient client;
        string method;
        object[] args;
        Callback callback;
        Timer timer;
        int count;

        public JsonRpcClientAsyncHelper(JsonRpcClient client, string method, Callback callback, params object[] args)
        {
            this.client = client;
            this.method = method;
            this.args = args;
            this.callback = callback;

            timer = new Timer
            {
                SynchronizingObject = (Form) PluginBase.MainForm,
                Interval = 1000
            };
            timer.Elapsed += OnTimer;
            count = 0;
            OnTimer();
        }

        void OnTimer(object sender = null, EventArgs e = null)
        {
            timer.Stop();

            Console.WriteLine("JsonRpcClientAsyncHelper's OnTimer(), method = {0}", method);

            if (COLTIsRunning())
            {
                // we are good to go
                try
                {
                    object result = client.Invoke(method, args);
                    if (callback != null) callback(result);
                }

                catch (Exception error)
                {
                    if (callback != null) callback(error);
                }

                return;
            }

            if (count++ > 13)
            {
                TraceManager.Add(LocaleHelper.GetString("Error.StartingCOLTTimedOut"), -1);
                return;
            }

            timer.Start();
        }

        bool COLTIsRunning()
        {
            try
            {
                client.Invoke("ping");
                return true;
            }
            catch (Exception)
            {
            }
            return false;
        }
    }
}
