using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ASCompletion.Context;
using ASCompletion.Model;
using ColtPlugin.Forms;
using ColtPlugin.Resources;
using ColtPlugin.Rpc;
using PluginCore;
using PluginCore.Helpers;
using PluginCore.Localization;
using PluginCore.Managers;
using PluginCore.Utilities;
using ProjectManager.Controls.TreeView;
using ProjectManager.Projects.AS3;
using ScintillaNet;
using Timer = System.Timers.Timer;

namespace ColtPlugin
{
	public class PluginMain : IPlugin
	{
        string pluginName = "ColtPlugin";
        string pluginGuid = "12600B5B-D185-4171-A362-25C5F73548C6";
        string pluginHelp = "codeorchestra.zendesk.com/home/";
        string pluginDesc = "COLT FD Plugin";
        string pluginAuth = "Makc"; // as if
        string settingFilename;
        Settings settingObject;
        ToolStripMenuItem menuItem, assetFolderAddItem, assetFolderRemoveItem;
        ToolStripButton exportToColt, openInColt;
        FileSystemWatcher watcher;
        string pathToLog;
        Timer timer;
        Keys makeItLiveKeys = Keys.Control | Keys.Shift | Keys.L;
        bool allowBuildInterception = true;
        int assetImageIndex = -1;
        TreeView projectTree;

	    #region Required Properties

        /// <summary>
        /// Api level of the plugin
        /// </summary>
        public int Api
        {
            get { return 1; }
        }

        /// <summary>
        /// Name of the plugin
        /// </summary> 
        public string Name
		{
			get { return pluginName; }
		}

        /// <summary>
        /// GUID of the plugin
        /// </summary>
        public string Guid
		{
			get { return pluginGuid; }
		}

        /// <summary>
        /// Author of the plugin
        /// </summary> 
        public string Author
		{
			get { return pluginAuth; }
		}

        /// <summary>
        /// Description of the plugin
        /// </summary> 
        public string Description
		{
			get { return pluginDesc; }
		}

        /// <summary>
        /// Web address for help
        /// </summary> 
        public string Help
		{
			get { return pluginHelp; }
		}

        /// <summary>
        /// Object that contains the settings
        /// </summary>
        [Browsable(false)]
        public object Settings
        {
            get { return settingObject; }
        }
		
		#endregion
		
		#region Required Methods

		/// <summary>
		/// Initializes the plugin
		/// </summary>
		public void Initialize()
		{
            InitBasics();
            LoadSettings();
            InitLocalization();
            AddEventHandlers();
        }
		
		/// <summary>
		/// Disposes the plugin
		/// </summary>
		public void Dispose()
		{
            SaveSettings();
		}
		
		/// <summary>
		/// Handles the incoming events
		/// </summary>
		public void HandleEvent(object sender, NotifyEvent e, HandlingPriority priority)
		{
            switch (e.Type)
            {
                case EventType.UIStarted:
                    DirectoryNode.OnDirectoryNodeRefresh += CreateAssetFoldersIcons;
                    break;

                case EventType.Command:
                    string cmd = ((DataEvent) e).Action;
                    switch (cmd)
                    {
                        case "ProjectManager.Project":
                            IProject project = PluginBase.CurrentProject;
                            bool as3ProjectIsOpen = project != null && project.Language == "as3";
                            if (menuItem != null) menuItem.Enabled = as3ProjectIsOpen;
                            if (exportToColt != null) exportToColt.Enabled = as3ProjectIsOpen;
                            if (openInColt != null) openInColt.Enabled = as3ProjectIsOpen && GetCOLTFile() != null;
                            // modified or new project - reconnect in any case
                            WatchErrorsLog();
                            break;
                        case "ProjectManager.Menu":
                            object menu = ((DataEvent) e).Data;
                            CreateMenuItem(menu as ToolStripMenuItem);
                            break;
                        case "ProjectManager.ToolBar":
                            ToolStrip toolStrip = ((DataEvent)e).Data as ToolStrip;
                            exportToColt = CreateToolbarButton(toolStrip, "colt_save.png", "Menu.ExportToCOLT", OnExportToColt);
                            openInColt = CreateToolbarButton(toolStrip, "colt_run.png", "Menu.OpenInCOLT", OnOpenInColt);
                            break;
                        case "ProjectManager.BuildingProject":
                        case "ProjectManager.TestingProject":
                            // todo: FD might send this for projects other than PluginBase.CurrentProject - figure out how to catch that
                            if (settingObject.InterceptBuilds && allowBuildInterception && openInColt.Enabled)
                            {
                                ProductionBuild(cmd == "ProjectManager.TestingProject");
                                e.Handled = true;
                            }
                            break;
                        case "ProjectManager.TreeSelectionChanged":
                            CreateContextMenuItems();
                            break;
                    }
                    break;
                
                case EventType.FileSave:
                    if (watcher.EnableRaisingEvents) ClearErrors();
                    break;

                case EventType.Keys: // shortcut pressed
                    KeyEvent ke = (KeyEvent)e;
                    if (ke.Value == makeItLiveKeys)
                    {
                        ke.Handled = true;
                        MakeItLive();
                    }
                    break;

                case EventType.Shortcut: // shortcut changed
                    DataEvent de = (DataEvent)e;
                    if (de.Action == "ColtPlugin.MakeItLive")
                    {
                        makeItLiveKeys = (Keys)de.Data;
                    }
                    break;

            }
		}

		#endregion

        #region Initialize() stuff

        /// <summary>
        /// Initializes important variables
        /// </summary>
        public void InitBasics()
        {
            string dataPath = Path.Combine(PathHelper.DataDir, "ColtPlugin");
            if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);
            settingFilename = Path.Combine(dataPath, "Settings.fdb");
        }

        /// <summary>
        /// Initializes the localization of the plugin
        /// </summary>
        public void InitLocalization()
        {
            LocaleVersion locale = PluginBase.MainForm.Settings.LocaleVersion;
            switch (locale)
            {
                /*
                case LocaleVersion.fi_FI : 
                    // We have Finnish available... or not. :)
                    LocaleHelper.Initialize(LocaleVersion.fi_FI);
                    break;
                */
                default : 
                    // Plugins should default to English...
                    LocaleHelper.Initialize(LocaleVersion.en_US);
                    break;
            }
            pluginDesc = LocaleHelper.GetString("Info.Description");
        }

        /// <summary>
        /// Adds the required event handlers
        /// </summary> 
        public void AddEventHandlers()
        {
            EventManager.AddEventHandler(this, EventType.UIStarted, HandlingPriority.High);
            EventManager.AddEventHandler(this, EventType.Command | EventType.FileSave | EventType.Keys | EventType.Shortcut);
            watcher = new FileSystemWatcher {NotifyFilter = NotifyFilters.LastWrite};
            watcher.Changed += OnFileChange;
            PluginBase.MainForm.RegisterShortcutItem("ColtPlugin.MakeItLive", makeItLiveKeys);
        }

        #endregion

        #region GetImage() stuff

        static Dictionary<string, Bitmap> imageCache = new Dictionary<string, Bitmap>();

        /// <summary>
        /// Gets embedded image from resources
        /// </summary>
        static Image GetImage(string imageName)
        {
            if (!imageCache.ContainsKey(imageName))
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                imageCache.Add(imageName, new Bitmap(assembly.GetManifestResourceStream(string.Format("ColtPlugin.Resources.{0}", imageName))));
            }

            return imageCache[imageName];
        }

        #endregion

        #region Menu items stuff

        void CreateAssetFoldersIcons(DirectoryNode node)
        {
            // we are going to save TreeView reference once we saw it
            // this hack comes from SourceControl's OverlayManager :S
            projectTree = node.TreeView;
            ImageList list = projectTree.ImageList;
            if (assetImageIndex < 0)
            {
                assetImageIndex = list.Images.Count;
                list.Images.Add(PluginBase.MainForm.FindImage("520"));
            }
            if (IsAssetFolder(node.BackingPath))
            {
                node.ImageIndex = assetImageIndex;
                node.SelectedImageIndex = assetImageIndex;
            }
        }

        void CreateContextMenuItems()
        {
            if (assetFolderAddItem == null)
            {
                //assetFolderAddItem = new ToolStripMenuItem(LocaleHelper.GetString("ContextMenu.AssetFolderAdd"), GetImage("colt_assets.png"));
                assetFolderAddItem = new ToolStripMenuItem(LocaleHelper.GetString("ContextMenu.AssetFolderAdd"), PluginBase.MainForm.FindImage("336"));
                assetFolderAddItem.Click += OnAssetAddOrRemoveClick;
                assetFolderRemoveItem = new ToolStripMenuItem(LocaleHelper.GetString("ContextMenu.AssetFolderRemove"))
                {
                    Checked = true
                };
                assetFolderRemoveItem.Click += OnAssetAddOrRemoveClick;
            }

            if ((projectTree != null) && !(projectTree.SelectedNode is ProjectNode))
            {
                DirectoryNode node = projectTree.SelectedNode as DirectoryNode;
                if (node != null)
                {
                    // good to go - insert after 1st separator
                    ContextMenuStrip menu = projectTree.ContextMenuStrip;
                    int index = 0;
                    while (index < menu.Items.Count)
                    {
                        index++;
                        if (menu.Items[index - 1] is ToolStripSeparator) break;
                    }
                    menu.Items.Insert(index, IsAssetFolder(node.BackingPath) ? assetFolderRemoveItem : assetFolderAddItem);
                }
            }
        }

        void OnAssetAddOrRemoveClick(object sender, EventArgs e)
        {
            DirectoryNode node = projectTree.SelectedNode as DirectoryNode;
            if (node != null)
            {
                List<string> assets = new List<string>(AssetFolders);
                if (assets.Contains(node.BackingPath)) assets.Remove(node.BackingPath); else assets.Add(node.BackingPath);
                AssetFolders = assets.ToArray();
                node.Refresh(false);
            }
        }

        void CreateMenuItem(ToolStripMenuItem projectMenu)
        {
            menuItem = new ToolStripMenuItem(LocaleHelper.GetString("Menu.ExportToCOLT"), GetImage("colt_save.png"),
                OnExportToColt, null) {Enabled = false};
            projectMenu.DropDownItems.Add(menuItem);
        }

        ToolStripButton CreateToolbarButton(ToolStrip toolStrip, string image, string hint, EventHandler handler)
        {
            ToolStripButton button = new ToolStripButton
            {
                Image = GetImage(image),
                Text = LocaleHelper.GetString(hint),
                DisplayStyle = ToolStripItemDisplayStyle.Image
            };
            button.Click += handler;
            toolStrip.Items.Add(button);
            return button;
        }

        void OnExportToColt(object sender, EventArgs e)
        {
            ExportAndOpen(settingObject.AutoRun);
        }

        void OnOpenInColt(object sender, EventArgs e)
        {
            FindAndOpen(settingObject.AutoRun);
        }

        #endregion

        #region Plugin settings stuff

        /// <summary>
        /// Loads the plugin settings
        /// </summary>
        public void LoadSettings()
        {
            string defaultExe = string.Format("{0}\\COLT\\colt.exe", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            settingObject = new Settings();
            if (!File.Exists(settingFilename))
            {
                settingObject.Executable = defaultExe;
                SaveSettings();
            }
            else
            {
                settingObject = (Settings)ObjectSerializer.Deserialize(settingFilename, settingObject);
                if (string.IsNullOrEmpty(settingObject.Executable))
                {
                    settingObject.Executable = defaultExe;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Saves the plugin settings
        /// </summary>
        public void SaveSettings()
        {
            ObjectSerializer.Serialize(settingFilename, settingObject);
        }

        /// <summary>
        /// Convenience property to get or set asset folders todo: extract this into something nice
        /// </summary>
        string[] AssetFolders
        {
            get
            {
                AS3Project project = PluginBase.CurrentProject as AS3Project;
                if (project != null && project.Storage.ContainsKey("colt.assets"))
                {
                    return project.Storage["colt.assets"].Split('|');
                }
                return new string[] { };
            }
            set
            {
                AS3Project project = PluginBase.CurrentProject as AS3Project;
                if (project == null) return;
                project.Storage["colt.assets"] = string.Join("|", value);
                project.Save();
            }
        }

        bool IsAssetFolder(string path)
        {
            return Array.IndexOf(AssetFolders, path) >= 0;
        }

		#endregion

        #region Logging errors

        /// <summary>
        /// Watches for COLT compilation errors log (optionally creates COLT folder if it does not exist)
        /// </summary>
        void WatchErrorsLog(bool createFolder = false)
        {
            // shut down errors log watcher and its timer
            watcher.EnableRaisingEvents = false;
            if (timer != null)
            {
                timer.Stop();
                timer = null;
            }
            // create the folder and subscribe to errors log updates
            IProject project = PluginBase.CurrentProject;
            if (project == null) return;
            string coltFolderPath = project.GetAbsolutePath(settingObject.WorkingFolder);
            if (createFolder && !Directory.Exists(coltFolderPath)) Directory.CreateDirectory(coltFolderPath);
            if (!Directory.Exists(coltFolderPath)) return;
            pathToLog = Path.Combine(coltFolderPath, "compile_errors.log");
            watcher.Path = coltFolderPath;
            watcher.EnableRaisingEvents = true;
        }

        void OnFileChange(object sender, FileSystemEventArgs e)
        {
            if (!e.FullPath.EndsWith("compile_errors.log") || timer != null) return;
            timer = new Timer
            {
                SynchronizingObject = (Form) PluginBase.MainForm,
                Interval = 200
            };
            // thread safe
            timer.Elapsed += OnTimerElapsed;
            timer.Enabled = true;
            timer.Start();
        }

	    void OnTimerElapsed(object sender, EventArgs e)
        {
            timer.Stop();
            timer = null;
            ClearErrors();
            string message = File.ReadAllText(pathToLog);
            // COLT copies sources to "incremental" folder, so let's try to find correct path and patch the output
            string incremental = "colt\\incremental";
            string[] sources = PluginBase.CurrentProject.SourcePaths;
            // send the log line by line
            string[] messageLines = message.Split('\r', '\n');
            bool hasErrors = false;
            foreach (string line in messageLines) if (line.Length > 0)
            {
                int errorLevel = -3;
                if (line.Contains(incremental))
                {
                    try
                    {
                        // carefully take the file name out
                        string file = line.Substring(0, line.IndexOf("): col"));
                        file = file.Substring(0, file.LastIndexOf("("));
                        file = file.Substring(file.IndexOf(incremental) + incremental.Length + 1);
                        // look for it in all source folders
                        foreach (string it in sources)
                        {
                            if (!File.Exists(PluginBase.CurrentProject.GetAbsolutePath(Path.Combine(it, file))))
                                continue;
                            TraceManager.Add(line.Replace(incremental, it), errorLevel);
                            hasErrors = true;
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        // unexpected format, send as is
                        TraceManager.Add(line, errorLevel);
                    }
                }
                else
                {
                    // send as is
                    TraceManager.Add(line, errorLevel);
                }
            }

            if (hasErrors) ShowErrors();
        }

        void ClearErrors()
        {
            EventManager.DispatchEvent(this, new DataEvent(EventType.Command, "ResultsPanel.ClearResults", null));
        }

        void ShowErrors()
        {
            // should be an option: if the panel was hidden it captures keyboard focus
            //EventManager.DispatchEvent(this, new DataEvent(EventType.Command, "ResultsPanel.ShowResults", null));
        }

        #endregion

        #region Meta tags

        /// <summary>
        /// Generate meta tags
        /// </summary>
        void MakeItLive()
        {
            ScintillaControl sci = PluginBase.MainForm.CurrentDocument.SciControl;
            if (sci == null) return;

            IASContext context = ASContext.Context;
            if (context.CurrentClass == null || context.CurrentClass.IsVoid() || context.CurrentClass.LineFrom == 0) 
                return;

            // make member live
            int originalPos = sci.CurrentPos;
            int pos;
            int line;
            string indent;
            MemberModel member = context.CurrentMember;
            FlagType mask = FlagType.Function | FlagType.Dynamic;
            if (member != null && (member.Flags & mask) == mask) 
            {
                line = context.CurrentMember.LineFrom;
                indent = LineIndentPosition(sci, line);
                pos = sci.PositionFromLine(line) + indent.Length;
                string insert = string.Format("[LiveCodeUpdateListener(method=\"{0}\")]\n{1}", member.Name, indent);
                sci.SetSel(pos, pos);
                sci.ReplaceSel(insert);
                originalPos += insert.Length;
            }

            // make class live
            if (!Regex.IsMatch(sci.Text, "\\[Live\\]"))
            {
                line = context.CurrentClass.LineFrom;
                indent = LineIndentPosition(sci, line);
                pos = sci.PositionFromLine(line) + indent.Length;
                string insert = string.Format("[Live]\n{0}", indent);
                sci.SetSel(pos, pos);
                sci.ReplaceSel(insert);
                originalPos += insert.Length;
            }

            sci.SetSel(originalPos, originalPos);
        }

        string LineIndentPosition(ScintillaControl sci, int line)
        {
            string txt = sci.GetLine(line);
            for (int i = 0; i < txt.Length; i++)
                if (txt[i] > 32) return txt.Substring(0, i);
            return "";
        }

        #endregion

        void GetSecurityToken()
        {
            // we expect this file to be open by now
            // because we get here from auth exception
            string coltFileName = GetCOLTFile();
            if (coltFileName == null) return;
            try
            {
                JsonRpcClient client = new JsonRpcClient(coltFileName);
                // knock
                client.Invoke("requestShortCode", "FlashDevelop");

                // if still here, user needs to enter the code
                FirstTimeDialog dialog = new FirstTimeDialog(settingObject.InterceptBuilds, settingObject.AutoRun);
                dialog.ShowDialog();
                // regardless of the code, set boolean options
                settingObject.AutoRun = dialog.AutoRun;
                settingObject.InterceptBuilds = dialog.InterceptBuilds;
                if (dialog.ShortCode != null && dialog.ShortCode.Length == 4)
                {
                    // short code looks right - request security token
                    settingObject.SecurityToken = client.Invoke("obtainAuthToken", dialog.ShortCode).ToString();
                }
            }
            catch (Exception details)
            {
                HandleAuthenticationExceptions(details);
            }
        }

        /// <summary>
        /// Makes production build and optionally runs its output
        /// </summary>
        void ProductionBuild(bool run)
        {
            // make sure the COLT project is open
            string path = FindAndOpen(false);
            if (path == null) return;
            try
            {
                JsonRpcClient client = new JsonRpcClient(path);
                client.PingOrRunCOLT(settingObject.Executable);
                // leverage FD launch mechanism
                if (run)
                {
                    client.InvokeAsync("runProductionCompilation", RunAfterProductionBuild, settingObject.SecurityToken, false);
                }
                else
                {
                    client.InvokeAsync("runProductionCompilation", HandleAuthenticationExceptions, settingObject.SecurityToken, false);
                }
            }
            catch (Exception details)
            {
                HandleAuthenticationExceptions(details);
            }
        }

        void RunAfterProductionBuild(object result)
        {
            Exception exception = result as Exception;
            if (exception != null) HandleAuthenticationExceptions(exception);
            else EventManager.DispatchEvent(this, new DataEvent(EventType.Command, "ProjectManager.PlayOutput", null));
        }

        /// <summary>
        /// Opens the project in COLT and optionally runs live session
        /// </summary>
        string FindAndOpen(bool run)
        {
            // Create COLT subfolder if does not exist yet
            // While at that, start listening for colt/compile_errors.log changes
            WatchErrorsLog(true);
            // Find COLT project to open
            string coltFileName = GetCOLTFile();
            // Open it
            if (coltFileName != null)
            {
                try
                {
                    if (run)
                    {
                        JsonRpcClient client = new JsonRpcClient(coltFileName);
                        client.PingOrRunCOLT(settingObject.Executable);
                        client.InvokeAsync("runBaseCompilation", HandleAuthenticationExceptions, settingObject.SecurityToken);
                    }
                    return coltFileName;
                }
                catch (Exception details)
                {
                    HandleAuthenticationExceptions(details);
                    return null;
                }
            }
            openInColt.Enabled = false;
            return null;
        }

        /// <summary>
        /// Exports the project to COLT and optionally runs live session
        /// </summary>
        void ExportAndOpen(bool run)
        {
            // Create COLT subfolder if does not exist yet
            // While at that, start listening for colt/compile_errors.log changes
            WatchErrorsLog(true);
            // Create COLT project in it
            COLTRemoteProject project = ExportCOLTProject();
            if (project == null) return;
            try
            {
                // Export the project as xml file
                project.Save();
                // Optionally run base compilation
                if (run)
                {
                    JsonRpcClient client = new JsonRpcClient(project.path);
                    client.PingOrRunCOLT(settingObject.Executable);
                    client.InvokeAsync("runBaseCompilation", HandleAuthenticationExceptions, settingObject.SecurityToken);
                }
                openInColt.Enabled = true;
                // Remove older *.colt files
                foreach (string oldFile in Directory.GetFiles(Path.GetDirectoryName(project.path), "*.colt"))
                {
                    if (!project.path.Contains(Path.GetFileName(oldFile)))
                    {
                        File.Delete(oldFile);
                    }
                }
            }
            catch (Exception details)
            {
                HandleAuthenticationExceptions(details);
            }
        }

        /// <summary>
        /// Handles possible authentication exceptions
        /// </summary>
        void HandleAuthenticationExceptions(object param)
        {
            Exception exception = param as Exception;
            if (exception == null) return;
            JsonRpcException rpcException = exception as JsonRpcException;
            if (rpcException != null)
            {
                // if the exception comes from rpc, we have two special situations to handle:
                // 1 short code was wrong (might happen a lot)
                // 2 security token was wrong (should never happen)
                // in both cases, we need to request new security token
                if ((rpcException.TypeName == "codeOrchestra.colt.core.rpc.security.InvalidShortCodeException") ||
                    (rpcException.TypeName == "codeOrchestra.colt.core.rpc.security.InvalidAuthTokenException"))
                {
                    settingObject.SecurityToken = null;
                        
                    // request new security token immediately
                    GetSecurityToken();
                }
            }
            else TraceManager.Add(exception.ToString(), -1);
        }

        /// <summary>
        /// Returns path to existing COLT project or null.
        /// </summary>
        string GetCOLTFile()
        {
            IProject project = PluginBase.CurrentProject;
            try
            {
                string[] files = Directory.GetFiles(project.GetAbsolutePath(settingObject.WorkingFolder), "*.colt");
                if (files.Length > 0) return files[0];
            }
            catch (Exception)
            {
            }
            return null;
        }

        /// <summary>
        /// Exports FD project setting to COLTRemoteProject instance.
        /// </summary>
        /// <returns></returns>
        COLTRemoteProject ExportCOLTProject()
        {
            // our options: parse project.ProjectPath (xml file) or use api
            AS3Project project = (AS3Project)PluginBase.CurrentProject;
            string configCopy = "";
            if (settingObject.FullConfig)
            {
                // Construct flex config file name (see AS3ProjectBuilder, line 140)
                string projectName = project.Name.Replace(" ", "");
                string configFile = Path.Combine("obj", string.Format("{0}Config.xml", projectName));
                if (!File.Exists(project.GetAbsolutePath(configFile)))
                {
                    TraceManager.Add(string.Format("Required file ({0}Config.xml) does not exist, project must be built first...", projectName), -1);
                    try
                    {
                        allowBuildInterception = false;
                        EventManager.DispatchEvent(this, new DataEvent(EventType.Command, "ProjectManager.BuildProject", null));
                    }
                    finally
                    {
                        allowBuildInterception = true;
                    }
                    return null;
                }
                // Create config copy with <file-specs>...</file-specs> commented out
                configCopy = Path.Combine("obj", string.Format("{0}ConfigCopy.xml", projectName));
                File.WriteAllText(project.GetAbsolutePath(configCopy),
                    File.ReadAllText(project.GetAbsolutePath(configFile))
                        .Replace("<file-specs", "<!-- file-specs")
                        .Replace("/file-specs>", "/file-specs -->"));
            }
            // Export COLT project
            COLTRemoteProject result = new COLTRemoteProject
            {
                path = project.GetAbsolutePath(Path.Combine(settingObject.WorkingFolder, string.Format("{0}.colt", System.Guid.NewGuid()))),
                name = project.Name
            };
            List<string> libraryPathsList = new List<string>(project.CompilerOptions.LibraryPaths);
            for (int i=0; i<libraryPathsList.Count; i++)
            {
                if (libraryPathsList[i].ToLower().EndsWith(".swc"))
                {
                    libraryPathsList[i] = string.Format(@"..\{0}", libraryPathsList[i]);
                }
                else
                {
                    // workaround (FD saves empty paths for missing libs)
                    libraryPathsList.RemoveAt(i);
                    i--;
                }
            }
            result.libraries = libraryPathsList.ToArray();
            result.targetPlayerVersion = string.Format("{0}.0", project.MovieOptions.Version);
            result.mainClass = project.GetAbsolutePath(project.CompileTargets[0]);
            result.flexSDKPath = project.CurrentSDK;
            if (settingObject.FullConfig) result.customConfigPath = project.GetAbsolutePath(configCopy);
            string outputPath = project.OutputPath;
            int lastSlash = outputPath.LastIndexOf(@"\");
            if (lastSlash > -1)
            {
                result.outputPath = project.GetAbsolutePath(outputPath.Substring(0, lastSlash));
                result.outputFileName = outputPath.Substring(lastSlash + 1);
            }
            else
            {
                result.outputPath = project.GetAbsolutePath("");
                result.outputFileName = outputPath;
            }

            string[] sourcePaths = project.SourcePaths.Clone() as string[];
            for (int i = 0; i < sourcePaths.Length; i++) sourcePaths[i] = string.Format(@"..\{0}", sourcePaths[i]);
            result.sources = sourcePaths;

            result.assets = AssetFolders;
            for (int i = 0; i < result.assets.Length; i++) result.assets[i] = string.Format(@"..\{0}", project.GetRelativePath(result.assets[i]));

            // size, frame rate and background color
            string[] coltAdditionalOptionsKeys = {
                "-default-size",
                "-default-frame-rate",
                "-default-background-color"
            };
            string[] coltAdditionalOptions = {
                string.Format("{0} {1} {2}", coltAdditionalOptionsKeys[0], project.MovieOptions.Width, project.MovieOptions.Height),
                string.Format("{0} {1}", coltAdditionalOptionsKeys[1], project.MovieOptions.Fps),
                string.Format("{0} {1}", coltAdditionalOptionsKeys[2], project.MovieOptions.BackgroundColorInt)
            };

            string additionalOptions = "";
            foreach (string option in project.CompilerOptions.Additional)
            {
                for (int i = 0; i < coltAdditionalOptionsKeys.Length; i++)
                {
                    if (option.Contains(coltAdditionalOptionsKeys[i]))
                    {
                        coltAdditionalOptions[i] = "";
                    }
                }
                additionalOptions += string.Format("{0} ", option);
            }

            foreach (string option in coltAdditionalOptions)
            {
                additionalOptions += string.Format("{0} ", option);
            }


            // compiler constants
            // see AddCompilerConstants in FDBuild's Building.AS3.FlexConfigWriter
            bool debugMode = project.TraceEnabled;
            bool isMobile = project.MovieOptions.Platform == "AIR Mobile";
            bool isDesktop = !isMobile && project.MovieOptions.Platform == "AIR";
            additionalOptions += string.Format("-define+=CONFIG::debug,{0} ", (debugMode ? "true" : "false"));
            additionalOptions += string.Format("-define+=CONFIG::release,{0} ", (debugMode ? "false" : "true"));
            additionalOptions += string.Format("-define+=CONFIG::timeStamp,\"'{0}'\" ", DateTime.Now.ToString("d"));
            additionalOptions += string.Format("-define+=CONFIG::air,{0} ", (isMobile || isDesktop ? "true" : "false"));
            additionalOptions += string.Format("-define+=CONFIG::mobile,{0} ", (isMobile ? "true" : "false"));
            additionalOptions += string.Format("-define+=CONFIG::desktop,{0} ", (isDesktop ? "true" : "false"));
            if (project.CompilerOptions.CompilerConstants != null)
            {
                foreach (string define in project.CompilerOptions.CompilerConstants)
                {
                    if (define.IndexOf(',') >= 0) additionalOptions += string.Format("-define+={0} ", define);
                }
            }
            result.compilerOptions = string.Format("{0}{1}", additionalOptions.Trim(), (debugMode ? " -debug" : ""));
            return result;
        }
	}
}