using System;
using System.ComponentModel;

namespace ColtPlugin
{
    [Serializable]
    public class Settings
    {
        public string SecurityToken;
        private bool autorun = true;
        private string workingFolder = "colt";

        /// <summary> 
        /// Get and sets colt.exe path
        /// </summary>
        [DisplayName("Path to COLT")]
        [Description("Path to COLT executable.")]
        public string Executable { get; set; }

        /// <summary> 
        /// Get and sets colt folder
        /// </summary>
        [DisplayName("COLT Working Folder")]
        [Description("Path to COLT working folder."), DefaultValue("colt")]
        public string WorkingFolder
        {
            get { return workingFolder; }
            set { workingFolder = value; }
        }

        /// <summary> 
        /// Get and sets full autorun flag
        /// </summary>
        [DisplayName("Automatically run COLT project")]
        [Description("Automatically compile and run COLT project after opening it in COLT."), DefaultValue(true)]
        public bool AutoRun
        {
            get { return autorun; }
            set { autorun = value; }
        }

        /// <summary> 
        /// Get and sets full config flag
        /// </summary>
        [DisplayName("Load Full FD Configuration")]
        [Description("Attempt to load full FD configuration in COLT. FD project must be built at least once first."), DefaultValue(false)]
        public bool FullConfig { get; set; }

        /// <summary> 
        /// Get and sets production builds flag
        /// </summary>
        [DisplayName("Use COLT for FD builds")]
        [Description("Use COLT fast compiler to build your FD projects."), DefaultValue(false)]
        public bool InterceptBuilds { get; set; }
    }
}