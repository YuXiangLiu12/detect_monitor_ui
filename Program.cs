namespace LeakMonitor;

static class Program
{
    /// <summary>
    /// 核电站泄漏监测系统 — 主入口
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 全局异常捕获，方便在无开发环境的机器上诊断启动问题
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            WriteErrorLog("UnhandledException", args.ExceptionObject?.ToString() ?? "未知错误");
        };

        Application.ThreadException += (sender, args) =>
        {
            WriteErrorLog("ThreadException", args.Exception.ToString());
        };

        try
        {
            // 高 DPI 支持
            ApplicationConfiguration.Initialize();

            // 启动主窗口
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            WriteErrorLog("StartupException", ex.ToString());
            MessageBox.Show($"程序启动失败：\n{ex.Message}\n\n详细信息已写入 error.log",
                "启动错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void WriteErrorLog(string type, string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "LeakMonitor_error.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{type}]\n{message}\n\n");
        }
        catch
        {
            // 写日志失败就放弃，不再抛异常
        }
    }
}
