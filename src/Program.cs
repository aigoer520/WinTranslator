using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinTranslator
{
    internal static class Program
    {
        // 代码级 DPI 感知：必须在创建任何窗口之前调用。
        // 旧版仅靠清单声明，缩放环境下定格图被放大、疑似牵连桌面图标显示；
        // 这里启动即声明 PerMonitorV2，清单作为后备。
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        [STAThread]
        private static void Main()
        {
            try
            {
                if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
                {
                    try { SetProcessDpiAwareness(2); } catch { }    // PROCESS_PER_MONITOR_DPI_AWARE
                }
            }
            catch
            {
                try { SetProcessDpiAwareness(2); } catch { }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
