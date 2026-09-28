using System;
using System.Diagnostics;
using CefSharp;

namespace FvmAssistant
{
    internal static class CefBootstrap
    {
        public static string FlashVersion { get; private set; }

        public static bool Initialize(AssistantOptions options, AppPaths paths, string flashPath)
        {
            CefSettingsBase settings = options.Mode == ViewMode.Offscreen
                ? new CefSharp.OffScreen.CefSettings()
                : (CefSettingsBase)new CefSharp.WinForms.CefSettings();

            // CEF's root is process-wide. Give each account its own root as well as cache so
            // separate FvmAssistant processes never share global browser state.
            settings.RootCachePath = paths.ProfileCache;
            settings.CachePath = paths.ProfileCache;
            settings.PersistSessionCookies = true;
            settings.PersistUserPreferences = true;
            settings.Locale = "zh-CN";
            settings.AcceptLanguageList = "zh-CN,zh";
            settings.LogFile = paths.CefLogFile;
            settings.LogSeverity = LogSeverity.Warning;

            FlashVersion = FileVersionInfo.GetVersionInfo(flashPath).FileVersion?.Replace(',', '.').Replace(" ", "");

            var args = settings.CefCommandLineArgs;
            args["ppapi-flash-path"] = flashPath;
            args["ppapi-flash-version"] = FlashVersion;
            args["enable-system-flash"] = "0";
            args["autoplay-policy"] = "no-user-gesture-required";
            // Game coordinates and templates are defined in page pixels, so pin the scale factor
            // regardless of the Windows display scaling. Only consistent in a DPI-unaware process:
            // a DPI-aware windowed browser sizes its view in DIPs and renders only part of the window.
            if (!options.DpiAware) args["force-device-scale-factor"] = "1";

            if (options.KeepRendering)
            {
                args["disable-features"] = "CalculateNativeWinOcclusion";
                args["disable-background-timer-throttling"] = "1";
                args["disable-renderer-backgrounding"] = "1";
                args["disable-backgrounding-occluded-windows"] = "1";
            }

            var resolverRules = DnsProbe.BuildResolverRules(DnsProbe.GameHosts, TimeSpan.FromSeconds(4));
            if (resolverRules != null)
            {
                args["host-resolver-rules"] = resolverRules;
                Log.Info("host-resolver-rules: " + resolverRules);
            }

            if (!options.Gpu)
            {
                args["disable-gpu"] = "1";
                args["disable-gpu-compositing"] = "1";
            }

            Log.Info($"flash {FlashVersion} at {flashPath}");
            return Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
        }

        /// <summary>
        /// Flash in Chromium 84 is click-to-play; allow it for every site before any page loads.
        /// Same preference CefFlashBrowser sets.
        /// </summary>
        public static void EnableFlashContent()
        {
            var task = Cef.UIThreadTaskFactory.StartNew(() =>
            {
                var context = Cef.GetGlobalRequestContext();
                SetPreference(context, "profile.default_content_setting_values.plugins", 1);
                SetPreference(context, "plugins.run_all_flash_in_allow_mode", true);
            });
            if (!task.Wait(TimeSpan.FromSeconds(10)))
                Log.Warn("timed out enabling Flash content settings");
        }

        private static void SetPreference(IRequestContext context, string name, object value)
        {
            if (context.SetPreference(name, value, out var error))
                Log.Info($"pref {name}={value}");
            else
                Log.Warn($"pref {name} failed: {error}");
        }
    }
}
