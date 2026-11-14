/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * Program.cs - application entry point.
 *
 *   ecat_drone_host.exe                 start the GUI
 *   ecat_drone_host.exe --simulate      start the GUI against the in-process
 *                                       firmware simulator (no hardware needed)
 *   ecat_drone_host.exe --selftest      run the protocol/logic self test on the
 *                                       console and exit (0 = pass, 1 = fail)
 *   ecat_drone_host.exe --help          show the command line help
 */

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using EcatDroneHost.App;
using EcatDroneHost.Ui;

namespace EcatDroneHost
{
    internal static class Program
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        /// <summary>Try to get a usable console for the winexe build.</summary>
        internal static bool AttachToParentConsole()
        {
            try
            {
                if (AttachConsole(AttachParentProcess))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                /* ignore */
            }
            /* if stdout was redirected by cmd.exe the handle already works */
            try
            {
                Console.Out.Flush();
                return !Console.IsOutputRedirected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            bool selfTest = HasFlag(args, "--selftest");
            bool uiTest = HasFlag(args, "--uitest");
            bool simulate = HasFlag(args, "--simulate") || HasFlag(args, "--sim");
            bool help = HasFlag(args, "--help") || HasFlag(args, "-h") || HasFlag(args, "/?");

            if (help)
            {
                AttachToParentConsole();
                Console.WriteLine(HelpText);
                return 0;
            }

            if (selfTest)
            {
                AttachToParentConsole();
                return SelfTest.Run(args);
            }

            if (uiTest)
            {
                AttachToParentConsole();
                return UiRuntimeTest.Run();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm(simulate));
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序启动失败:\r\n" + ex, "ecat_drone_host",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null)
            {
                return false;
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string HelpText
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("ecat_drone_host - ecat_drone_powertrain 上位机 (C# WinForms)");
                sb.AppendLine();
                sb.AppendLine("用法:");
                sb.AppendLine("  ecat_drone_host.exe                启动图形界面");
                sb.AppendLine("  ecat_drone_host.exe --simulate     使用内置模拟设备启动（无硬件演示）");
                sb.AppendLine("  ecat_drone_host.exe --selftest     运行协议/逻辑自检并退出（0=通过）");
                sb.AppendLine("  ecat_drone_host.exe --uitest       界面运行检查（模拟设备，需要桌面）");
                sb.AppendLine("  ecat_drone_host.exe --help         显示本帮助");
                sb.AppendLine();
                sb.AppendLine("可选参数:");
                sb.AppendLine("  --port=COM3     启动时自动连接该串口");
                sb.AppendLine("  --selftest-log=<文件>  自检报告另存路径（默认为 exe 同目录 selftest.log）");
                sb.AppendLine();
                sb.AppendLine("协议: A5 5A | CMD | SEQ | LEN(L,H) | PAYLOAD | CRC16-CCITT-FALSE(L,H)");
                sb.AppendLine("详见 README_zh.md 与项目文档 docs/HOST_TOOL.md。");
                return sb.ToString();
            }
        }
    }
}
