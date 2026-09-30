using System;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;

namespace FvmAssistant
{
    internal enum ViewMode
    {
        /// <summary>Independent account and task controller; does not initialize CEF.</summary>
        Controller,
        /// <summary>Normal CEF child window (what CefFlashBrowser uses).</summary>
        Windowed,
        /// <summary>Windowless rendering: frames arrive in memory, we paint them ourselves.</summary>
        Offscreen,
    }

    internal sealed class AssistantOptions
    {
        public const string DefaultUrl = "https://my.4399.com/yxmsdzls/wd-air";

        public const string Usage =
            "FvmAssistant.exe [--mode controller|windowed|osr] [--profile <name>] [--url <url>] [--port <n>]\n" +
            "             [--flash <pepflashplayer.dll>] [--viewport <W>x<H>] [--no-gpu] [--allow-throttling] [--dpi-aware]\n" +
            "             [--track-dir <轨道编辑器数据目录>]";

        public ViewMode Mode { get; private set; } = ViewMode.Controller;
        public string Profile { get; private set; } = "main";
        public string Url { get; private set; } = DefaultUrl;
        public int Port { get; private set; } = 18765;
        public string FlashPath { get; private set; }
        public Size Viewport { get; private set; } = new Size(1280, 800);
        public bool Gpu { get; private set; } = true;

        /// <summary>Pass Chromium switches that stop it pausing hidden/occluded pages.</summary>
        public bool KeepRendering { get; private set; } = true;

        /// <summary>
        /// Per-monitor DPI aware and rendered at the monitor's scale (like CefFlashBrowser).
        /// Off by default: a DPI-unaware process rendered at scale 1 keeps page pixels equal to
        /// Flash stage pixels on any display, which is what capture/templates rely on.
        /// </summary>
        public bool DpiAware { get; private set; }

        /// <summary>Folder holding 轨道编辑器's 附加程序 and 预制轨道 (defaults to the repo's vendor\track).</summary>
        public string TrackDir { get; private set; }

        public static AssistantOptions Parse(string[] args)
        {
            var o = new AssistantOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} 缺少参数值");

                switch (args[i])
                {
                    case "--mode":
                        var mode = Next();
                        if (mode == "controller") o.Mode = ViewMode.Controller;
                        else if (mode == "windowed") o.Mode = ViewMode.Windowed;
                        else if (mode == "osr" || mode == "offscreen") o.Mode = ViewMode.Offscreen;
                        else throw new ArgumentException("--mode 只能是 controller、windowed 或 osr");
                        break;
                    case "--profile":
                        o.Profile = Next();
                        if (!Regex.IsMatch(o.Profile, "^[A-Za-z0-9_-]{1,32}$"))
                            throw new ArgumentException("--profile 只能包含字母、数字、_ 和 -");
                        break;
                    case "--url":
                        o.Url = Next();
                        if (!UrlText.IsWeb(o.Url)) throw new ArgumentException("--url 必须是 http(s) 地址");
                        break;
                    case "--port":
                        if (!int.TryParse(Next(), out var port) || port < 1024 || port > 65535)
                            throw new ArgumentException("--port 必须在 1024-65535 之间");
                        o.Port = port;
                        break;
                    case "--flash":
                        o.FlashPath = Path.GetFullPath(Next());
                        break;
                    case "--viewport":
                        var m = Regex.Match(Next(), @"^(\d{3,4})x(\d{3,4})$");
                        if (!m.Success) throw new ArgumentException("--viewport 格式为 1280x800");
                        o.Viewport = new Size(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
                        break;
                    case "--no-gpu":
                        o.Gpu = false;
                        break;
                    case "--allow-throttling":
                        o.KeepRendering = false;
                        break;
                    case "--dpi-aware":
                        o.DpiAware = true;
                        break;
                    case "--track-dir":
                        o.TrackDir = Path.GetFullPath(Next());
                        break;
                    default:
                        throw new ArgumentException("未知参数 " + args[i]);
                }
            }
            return o;
        }

        /// <summary>--track-dir, else the nearest "vendor\track" above the executable.</summary>
        public string ResolveTrackDir()
        {
            if (TrackDir != null) return Directory.Exists(TrackDir) ? TrackDir : null;
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "vendor", "track");
                if (Directory.Exists(Path.Combine(candidate, "附加程序"))) return candidate;
            }
            return null;
        }

        public string ResolveFlashPath()
        {
            if (FlashPath != null) return File.Exists(FlashPath) ? FlashPath : null;
            var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "pepflashplayer.dll");
            return File.Exists(bundled) ? bundled : null;
        }
    }
}
