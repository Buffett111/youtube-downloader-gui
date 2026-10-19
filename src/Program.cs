using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace SimpleDownloader
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            string user = WindowsIdentity.GetCurrent().User.Value;
            using (var instance = new Mutex(true, "Local\\SimpleDownloader." + user, out created))
            {
                if (!created)
                {
                    MessageBox.Show("下載器已經開啟，請切換至現有視窗。", "影片與音樂下載器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show(e.Exception.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                Application.Run(new MainForm());
            }
        }
    }
}
