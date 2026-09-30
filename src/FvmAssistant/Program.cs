using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using CefSharp;
using FvmAssistant.Controller;

namespace FvmAssistant
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AssistantOptions options;
            try
            {
                options = AssistantOptions.Parse(args);
            }
            catch (ArgumentException e)
            {
                MessageBox.Show(e.Message + "\n\n" + AssistantOptions.Usage, "FvmAssistant");
                return 2;
            }

            if (options.Mode == ViewMode.Controller)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    using (var form = new ControllerForm()) Application.Run(form);
                    return 0;
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message, "FvmAssistant 控制台");
                    return 7;
                }
            }

            // CEF profiles cannot safely be opened by two processes at once. The account name is
            // already restricted to ASCII letters, numbers, '_' and '-' by AssistantOptions.Parse.
            using (var profileMutex = new Mutex(false, @"Local\FvmAssistant.Profile." + options.Profile.ToLowerInvariant()))
            {
                bool acquired;
                try { acquired = profileMutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    MessageBox.Show("这个 profile 已由另一个 FvmAssistant 实例使用。请为第二个账户指定不同的 --profile。", "FvmAssistant");
                    return 6;
                }
                try { return Run(options); }
                finally { profileMutex.ReleaseMutex(); }
            }
        }

        private static int Run(AssistantOptions options)
        {
            if (options.DpiAware) Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

            var paths = new AppPaths(options.Profile);
            Log.Init(paths.LogFile);
            Log.Info($"start pid={Process.GetCurrentProcess().Id} mode={options.Mode} profile={options.Profile} port={options.Port} " +
                     $"gpu={options.Gpu} keepRendering={options.KeepRendering} dpiAware={options.DpiAware}");
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("unhandled", e.ExceptionObject as Exception);

            var flashPath = options.ResolveFlashPath();
            if (flashPath == null)
            {
                MessageBox.Show("找不到 pepflashplayer.dll。\n先运行 scripts\\fetch-flash-plugin.ps1，或用 --flash <路径> 指定。", "FvmAssistant");
                return 3;
            }

            if (!CefBootstrap.Initialize(options, paths, flashPath))
            {
                MessageBox.Show("CEF 初始化失败，详见日志：\n" + paths.LogFile, "FvmAssistant");
                return 4;
            }
            CefBootstrap.EnableFlashContent();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Log.Error("ui thread", e.Exception);

            try
            {
                using (var form = new MainForm(options, CefBootstrap.FlashVersion))
                {
                    var grind = new GrindController(form, paths, options);
                    form.AttachGrind(grind, paths.PanelSettings);
                    using (options.Mode == ViewMode.Offscreen ? new LoaderWatchdog(form, grind) : null)
                    using (var server = new ControlServer(options.Port, new AssistantApi(form, paths, options, grind).HandleAsync))
                    {
                        try
                        {
                            server.Start();
                        }
                        catch (SocketException e)
                        {
                            MessageBox.Show($"无法监听 127.0.0.1:{options.Port}（{e.Message}）。\n换一个端口：--port <n>", "FvmAssistant");
                            return 5;
                        }
                        Application.Run(form);
                    }
                }
            }
            finally
            {
                Cef.Shutdown();
                Log.Info("exit");
            }
            return 0;
        }
    }
}
