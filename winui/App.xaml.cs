using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace DouyinQualityHelper
{
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }

        public App()
        {
            InitializeComponent();
            UnhandledException += (s, e) =>
            {
                AppPaths.WriteCrashLog(e.Exception);
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // 命令行模式（--selftest / --patch / --kill / --version）走控制台，不弹窗。
            // WinUI 的 Main 由 XAML 编译器生成，这里从 Environment 取一次参数。
            string[] argv = Environment.GetCommandLineArgs();
            if (argv.Length > 1)
            {
                int code = CliRunner.Run(argv);
                Environment.Exit(code);
                return;
            }

            var win = new MainWindow();
            MainWindow = win;
            win.Activate();
        }
    }
}
