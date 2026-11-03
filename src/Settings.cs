// 设置持久化
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeCleaner
{
    public sealed class AppSettings
    {
        public bool IncludeDownloads = true;
        public bool DeleteToRecycleBin = true;
        public bool TrayAutoScan = true;
        public bool ConfirmBeforeQuarantine = true;
        /// <summary>允许用 explorer.exe /select 定位文件。会新建进程，在部分机器上可能触发 0xc0000142，默认关闭。</summary>
        public bool UseExplorerSelect = false;
        public List<string> ExtraLibraries = new List<string>();
        public string WeInstallDir;

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            string p = AppPaths.SettingsFile;
            if (!File.Exists(p)) return s;
            try
            {
                Dictionary<string, object> d = Json.Obj(Json.ParseFile(p));
                if (d == null) return s;
                s.IncludeDownloads = Json.GetBool(d, "includeDownloads", s.IncludeDownloads);
                s.DeleteToRecycleBin = Json.GetBool(d, "deleteToRecycleBin", s.DeleteToRecycleBin);
                s.TrayAutoScan = Json.GetBool(d, "trayAutoScan", s.TrayAutoScan);
                s.ConfirmBeforeQuarantine = Json.GetBool(d, "confirmBeforeQuarantine", s.ConfirmBeforeQuarantine);
                s.UseExplorerSelect = Json.GetBool(d, "useExplorerSelect", s.UseExplorerSelect);
                s.WeInstallDir = Json.GetStr(d, "weInstallDir", null);

                object libsObj;
                if (d.TryGetValue("extraLibraries", out libsObj))
                {
                    List<object> arr = Json.Arr(libsObj);
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Count; i++)
                        {
                            string v = arr[i] as string;
                            if (!string.IsNullOrEmpty(v)) s.ExtraLibraries.Add(v);
                        }
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["includeDownloads"] = IncludeDownloads;
            d["deleteToRecycleBin"] = DeleteToRecycleBin;
            d["trayAutoScan"] = TrayAutoScan;
            d["confirmBeforeQuarantine"] = ConfirmBeforeQuarantine;
            d["useExplorerSelect"] = UseExplorerSelect;
            d["weInstallDir"] = WeInstallDir;
            List<object> libs = new List<object>();
            for (int i = 0; i < ExtraLibraries.Count; i++) libs.Add(ExtraLibraries[i]);
            d["extraLibraries"] = libs;
            d["updated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            try
            {
                File.WriteAllText(AppPaths.SettingsFile, Json.Serialize(d), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
