using System;
using System.Windows.Forms;

namespace ColtPlugin.Forms
{
    public partial class FirstTimeDialog : Form
    {
        public bool AutoRun = true;
        public bool InterceptBuilds;
        public string ShortCode;

        public FirstTimeDialog()
        {
            InitializeComponent();
        }

        public FirstTimeDialog(bool interceptBuilds, bool autorun)
        {
            InitializeComponent();
            InterceptBuilds = checkBox1.Checked = interceptBuilds;
            AutoRun = checkBox2.Checked = autorun;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            AutoRun = checkBox2.Checked;
            InterceptBuilds = checkBox1.Checked;
            ShortCode = textBox1.Text;

            Close();
        }
    }
}
