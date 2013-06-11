using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Reflection;
using WeifenLuo.WinFormsUI.Docking;
using ColtPlugin.Resources;
using PluginCore.Localization;
using PluginCore.Utilities;
using PluginCore.Managers;
using PluginCore.Helpers;
using PluginCore;
using ProjectManager.Projects.AS3;
using ASCompletion.Context;
using System.Text.RegularExpressions;
using ASCompletion.Model;

namespace ColtPlugin
{
	public class PluginMain : IPlugin
	{
        private String pluginName = "ColtPlugin";
        private String pluginGuid = "12600B5B-D185-4171-A362-25C5F73548C6";
        private String pluginHelp = "makc3d.wordpress.com/about/";
        private String pluginDesc = "COLT FD Plugin";
        private String pluginAuth = "Makc"; // as if
        private String settingFilename;
        private Settings settingObject;
        private ToolStripMenuItem menuItem;
        private ToolStripButton toolbarButton, toolbarButton2;
        private FileSystemWatcher watcher;
        private String pathToLog;
        private System.Timers.Timer timer;
        private Keys MakeItLiveKeys = Keys.Control | Keys.Shift | Keys.L;

	    #region Required Properties

        /// <summary>
        /// Api level of the plugin
        /// </summary>
        public Int32 Api
        {
            get { return 1; }
        }

        /// <summary>
        /// Name of the plugin
        /// </summary> 
        public String Name
		{
			get { return pluginName; }
		}

        /// <summary>
        /// GUID of the plugin
        /// </summary>
        public String Guid
		{
			get { return pluginGuid; }
		}

        /// <summary>
        /// Author of the plugin
        /// </summary> 
        public String Author
		{
			get { return pluginAuth; }
		}

        /// <summary>
        /// Description of the plugin
        /// </summary> 
        public String Description
		{
			get { return pluginDesc; }
		}

        /// <summary>
        /// Web address for help
        /// </summary> 
        public String Help
		{
			get { return pluginHelp; }
		}

        /// <summary>
        /// Object that contains the settings
        /// </summary>
        [Browsable(false)]
        public Object Settings
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
		public void HandleEvent(Object sender, NotifyEvent e, HandlingPriority prority)
		{
            switch (e.Type)
            {
                case EventType.Command:
                    string cmd = (e as DataEvent).Action;
                    if (cmd == "ProjectManager.Project")
                    {
                        IProject project = PluginBase.CurrentProject;
                        Boolean as3projectIsOpen = (project != null) && (project.Language == "as3");
                        if (menuItem != null) menuItem.Enabled = as3projectIsOpen;
                        if (toolbarButton != null) toolbarButton.Enabled = as3projectIsOpen;
                        if (toolbarButton2 != null) toolbarButton2.Enabled = as3projectIsOpen && (GetCOLTFile() != null);
                        // modified or new project - reconnect in any case
                        ConnectToCOLT();
                    }
                    else if (cmd == "ProjectManager.Menu")
                    {
                        Object menu = (e as DataEvent).Data;
                        CreateMenuItem(menu as ToolStripMenuItem);
                    }
                    else if (cmd == "ProjectManager.ToolBar")
                    {
                        Object toolStrip = (e as DataEvent).Data;
                        toolbarButton = CreateToolbarButton(toolStrip as ToolStrip, "colt_save.png", "Menu.ExportToCOLT", new EventHandler(OnClick));
                        toolbarButton2 = CreateToolbarButton(toolStrip as ToolStrip, "colt_run.png", "Menu.OpenInCOLT", new EventHandler(OnClick2));
                    }
                    break;
                
                case EventType.FileSave:
                    if (watcher.EnableRaisingEvents) ClearErrors();
                    break;

                case EventType.Keys: // shortcut pressed
                    KeyEvent ke = (KeyEvent)e;
                    if (ke.Value == MakeItLiveKeys)
                    {
                        ke.Handled = true;
                        MakeItLive();
                    }
                    break;

                case EventType.Shortcut: // shortcut changed
                    DataEvent de = (DataEvent)e;
                    if (de.Action == "ColtPlugin.MakeItLive")
                    {
                        MakeItLiveKeys = (Keys)de.Data;
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
            String dataPath = Path.Combine(PathHelper.DataDir, "ColtPlugin");
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
            EventManager.AddEventHandler(this, EventType.Command | EventType.FileSave | EventType.Keys | EventType.Shortcut);

            watcher = new FileSystemWatcher();
            watcher.NotifyFilter = NotifyFilters.LastWrite;
            watcher.Changed += new FileSystemEventHandler(OnFileChange);

            PluginBase.MainForm.RegisterShortcutItem("ColtPlugin.MakeItLive", MakeItLiveKeys);
        }

        #endregion

        #region Menu items stuff

        private void CreateMenuItem(ToolStripMenuItem projectMenu)
        {
            menuItem = new ToolStripMenuItem(LocaleHelper.GetString("Menu.ExportToCOLT"), GetImage("colt_save.png"), new EventHandler(OnClick), null);
            menuItem.Enabled = false;
            projectMenu.DropDownItems.Add(menuItem);
        }

        private ToolStripButton CreateToolbarButton(ToolStrip toolStrip, String image, String hint, EventHandler handler)
        {
            ToolStripButton button = new ToolStripButton();
            button.Image = GetImage(image);
            button.Text = LocaleHelper.GetString(hint);
            button.DisplayStyle = ToolStripItemDisplayStyle.Image;
            button.Click += handler;
            toolStrip.Items.Add(button);
            return button;
        }

        /// <summary>
        /// Gets embedded image from resources
        /// </summary>
        private static Image GetImage(String imageName)
        {
            imageName = "ColtPlugin.Resources." + imageName;
            Assembly assembly = Assembly.GetExecutingAssembly();
            return new Bitmap(assembly.GetManifestResourceStream(imageName));
        }

        private void OnClick(Object sender, System.EventArgs e)
        {
            OpenInCOLT();
        }

        private void OnClick2(Object sender, System.EventArgs e)
        {
            OpenInCOLT(false);
        }

        #endregion

        #region Plugin settings stuff

        /// <summary>
        /// Loads the plugin settings
        /// </summary>
        public void LoadSettings()
        {
            settingObject = new Settings();
            if (!File.Exists(settingFilename)) SaveSettings();
            else
            {
                Object obj = ObjectSerializer.Deserialize(settingFilename, settingObject);
                settingObject = (Settings)obj;
            }
        }

        /// <summary>
        /// Saves the plugin settings
        /// </summary>
        public void SaveSettings()
        {
            ObjectSerializer.Serialize(settingFilename, settingObject);
        }

		#endregion

        #region Logging errors

        private void OnFileChange(Object sender, FileSystemEventArgs e)
        {
            if (e.FullPath.EndsWith("compile_errors.log"))
            {
                if (timer == null)
                {
                    timer = new System.Timers.Timer();
                    timer.SynchronizingObject = (Form)PluginBase.MainForm; // thread safe
                    timer.Interval = 200;
                    timer.Elapsed += OnTimerElapsed;
                    timer.Enabled = true;
                    timer.Start();
                }
            }
        }

        private void OnTimerElapsed(object sender, EventArgs e)
        {
            timer.Stop();
            timer = null;

            ClearErrors();

            String message = File.ReadAllText(pathToLog);

            // COLT copies sources to "incremental" folder, so let's try to find correct path and patch the output
            String incremental = "colt\\incremental";
            String[] sources = PluginBase.CurrentProject.SourcePaths;

            // send the log line by line
            String[] messageLines = message.Split(new Char[] {'\r', '\n'});
            bool hasErrors = false;
            foreach (String line in messageLines) if (line.Length > 0)
            {
                int errorLevel = -3;
                if (line.Contains(incremental))
                {
                    try
                    {
                        // carefully take the file name out
                        String file = line.Substring(0, line.IndexOf("): col"));
                        file = file.Substring(0, file.LastIndexOf("("));
                        file = file.Substring(file.IndexOf(incremental) + incremental.Length + 1);

                        // look for it in all source folders
                        for (int i = 0; i < sources.Length; i++)
                        {
                            if (File.Exists(PluginBase.CurrentProject.GetAbsolutePath(Path.Combine(sources[i], file))))
                            {
                                TraceManager.Add(line.Replace(incremental, sources[i]), errorLevel);
                                hasErrors = true;
                                break;
                            }
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

        private void ClearErrors()
        {
            EventManager.DispatchEvent(this, new DataEvent(EventType.Command, "ResultsPanel.ClearResults", null));
        }

        private void ShowErrors()
        {
            // should be an option: if the panel was hidden it captures keyboard focus
            //EventManager.DispatchEvent(this, new DataEvent(EventType.Command, "ResultsPanel.ShowResults", null));
        }

        #endregion

        #region Meta tags

        /// <summary>
        /// Generate meta tags
        /// </summary>
        private void MakeItLive()
        {
            ScintillaNet.ScintillaControl sci = PluginBase.MainForm.CurrentDocument.SciControl;
            if (sci == null) 
                return;

            IASContext context = ASCompletion.Context.ASContext.Context;
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
                string insert = "[LiveCodeUpdateListener(method=\"" + member.Name + "\")]\n" + indent;
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
                string insert = "[Live]\n" + indent;
                sci.SetSel(pos, pos);
                sci.ReplaceSel(insert);
                originalPos += insert.Length;
            }

            sci.SetSel(originalPos, originalPos);
        }

        private string LineIndentPosition(ScintillaNet.ScintillaControl sci, int line)
        {
            string txt = sci.GetLine(line);
            for (int i = 0; i < txt.Length; i++)
                if (txt[i] > 32) return txt.Substring(0, i);
            return "";
        }

        #endregion

        /// <summary>
        /// Connects to COLT
        /// </summary>
        private void ConnectToCOLT(Boolean create = false)
        {
            // todo: clean up after previous connection

            // for now, shut down errors log watcher and its timer
            watcher.EnableRaisingEvents = false;
            if (timer != null) { timer.Stop(); timer = null; }

            // todo: if current project is opened in COLT - connect to it

            // for now, create the folder and subscribe to errors log updates
            IProject project = PluginBase.CurrentProject;

            String coltFolderPath = project.GetAbsolutePath(settingObject.WorkingFolder);
            if (create && !Directory.Exists(coltFolderPath)) Directory.CreateDirectory(coltFolderPath);

            if (Directory.Exists(coltFolderPath))
            {
                pathToLog = Path.Combine(coltFolderPath, "compile_errors.log");
                watcher.Path = coltFolderPath;
                watcher.EnableRaisingEvents = true;
            }
        }

        /// <summary>
        /// Opens the project in COLT
        /// </summary>
        private void OpenInCOLT(Boolean create = true)
        {
            // Create COLT subfolder if does not exist yet
            // While at that, start listening for colt/compile_errors.log changes
            ConnectToCOLT(true);


            // Find or create COLT project to open
            String coltFileName = create ? ExportCOLTFile() : GetCOLTFile();


            // Open it with default app (COLT)
            try
            {
                if (coltFileName != null)
                {
                    Process.Start(coltFileName);
                }

                else
                {
                    toolbarButton2.Enabled = false;
                }
            }

            catch (Exception e)
            {
                TraceManager.Add("Could not start COLT: " + e.ToString());
            }

        }

        /// <summary>
        /// Returns path to existing COLT project or null.
        /// </summary>
        private String GetCOLTFile()
        {
            IProject project = PluginBase.CurrentProject;

            try
            {
                String[] files = Directory.GetFiles(project.GetAbsolutePath(settingObject.WorkingFolder), "*.colt");
                if (files.Length > 0) return files[0];
            }

            catch (Exception)
            {
            }

            return null;
        }

        /// <summary>
        /// Exports the project to COLT and returns path to it or null.
        /// </summary>
        private String ExportCOLTFile()
        {
            // our options: parse project.ProjectPath (xml file) or use api
            AS3Project project = (AS3Project)PluginBase.CurrentProject;

            String configCopy = "";
            if (settingObject.FullConfig)
            {
                // Construct flex config file name (see AS3ProjectBuilder, line 140)
                String projectName = project.Name.Replace(" ", "");
                String configFile = Path.Combine("obj", projectName + "Config.xml");

                if (!File.Exists(project.GetAbsolutePath(configFile)))
                {
                    TraceManager.Add("Required file (" + projectName + "Config.xml) does not exist, project must be built first...", -1);

                    EventManager.DispatchEvent(this, new DataEvent(EventType.Command, "ProjectManager.BuildProject", null));

                    return null;
                }

                // Create config copy with <file-specs>...</file-specs> commented out
                configCopy = Path.Combine("obj", projectName + "ConfigCopy.xml");
                File.WriteAllText(project.GetAbsolutePath(configCopy),
                    File.ReadAllText(project.GetAbsolutePath(configFile))
                        .Replace("<file-specs", "<!-- file-specs")
                        .Replace("/file-specs>", "/file-specs -->"));
            }


            // Create COLT project with random name
            String coltFileName = project.GetAbsolutePath(Path.Combine(settingObject.WorkingFolder, System.Guid.NewGuid() + ".colt"));
            StreamWriter stream = File.CreateText(coltFileName);


            // Write current project settings there
            stream.WriteLine("#Generated by FD plugin");

            stream.WriteLine("name=" + project.Name);

            MxmlcOptions options = project.CompilerOptions;
            String libraryPaths = "";
            foreach (String libraryPath in options.LibraryPaths)
                libraryPaths += EscapeForCOLT(project.GetAbsolutePath(libraryPath)) + ";";
            stream.WriteLine("libraryPaths=" + libraryPaths);

            stream.WriteLine("clearMessages=true");

            stream.WriteLine("targetPlayerVersion=" + project.MovieOptions.Version + ".0");

            stream.WriteLine("mainClass=" + EscapeForCOLT(project.GetAbsolutePath(project.CompileTargets[0])));

            stream.WriteLine("maxLoopIterations=10000");

            stream.WriteLine("flexSDKPath=" + EscapeForCOLT(project.CurrentSDK));

            stream.WriteLine("liveMethods=annotated");

            if (settingObject.FullConfig)
            {
                stream.WriteLine("useCustomSDKConfiguration=true");
                stream.WriteLine("customConfigPath=" + EscapeForCOLT(project.GetAbsolutePath(configCopy)) + "\"");
            }

            stream.WriteLine("target=SWF"); // use project.MovieOptions.Platform switch ??

            String outputPath = project.OutputPath;
            int lastSlash = outputPath.LastIndexOf(@"\");
            if (lastSlash > -1)
            {
                stream.WriteLine("outputPath=" + EscapeForCOLT(project.GetAbsolutePath(outputPath.Substring(0, lastSlash))));
                stream.WriteLine("outputFileName=" + outputPath.Substring(lastSlash + 1));
            }

            else
            {
                stream.WriteLine("outputFileName=" + outputPath);
            }

            stream.WriteLine("useDefaultSDKConfiguration=true");

            String sourcePaths = "";
            foreach (String sourcePath in project.SourcePaths)
                sourcePaths += EscapeForCOLT(project.GetAbsolutePath(sourcePath)) + ";";
            stream.WriteLine("sourcePaths=" + sourcePaths);


            // size, frame rate and background color
            String[] coltAdditionalOptionsKeys = {
                "-default-size",
                "-default-frame-rate",
                "-default-background-color"
            };
            String[] coltAdditionalOptions = {
                coltAdditionalOptionsKeys[0] + " " + project.MovieOptions.Width + " " + project.MovieOptions.Height,
                coltAdditionalOptionsKeys[1] + " " + project.MovieOptions.Fps,
                coltAdditionalOptionsKeys[2] + " " + project.MovieOptions.BackgroundColorInt
            };

            String additionalOptions = "";
            foreach (String option in project.CompilerOptions.Additional)
            {
                for (int i = 0; i < coltAdditionalOptionsKeys.Length; i++)
                {
                    if (option.Contains(coltAdditionalOptionsKeys[i]))
                    {
                        coltAdditionalOptions[i] = "";
                    }
                }
                additionalOptions += option + " ";
            }

            foreach (String option in coltAdditionalOptions)
            {
                additionalOptions += option + " ";
            }

            stream.WriteLine("compilerOptions=" + additionalOptions.Trim());

            stream.Close();


            // Remove older *.colt files
            foreach (String oldFile in Directory.GetFiles(project.GetAbsolutePath(settingObject.WorkingFolder), "*.colt"))
            {
                if (!coltFileName.Contains(Path.GetFileName(oldFile)))
                {
                    File.Delete(oldFile);
                }
            }


            // Enable "open" button
            toolbarButton2.Enabled = true;


            return coltFileName;
        }

        private String EscapeForCOLT(String path)
        {
            if (path == null) return "";
            // some standard escape ??
            return path.Replace(@"\", @"\\").Replace(":", @"\:").Replace("=", @"\=");
        }
	}
}
