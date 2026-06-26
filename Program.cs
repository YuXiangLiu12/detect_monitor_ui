namespace LeakMonitor;

static class Program
{
    /// <summary>
    /// 核电站泄漏监测系统 — 主入口
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 高 DPI 支持
        ApplicationConfiguration.Initialize();

        // 启动主窗口
        Application.Run(new MainForm());
    }
}
