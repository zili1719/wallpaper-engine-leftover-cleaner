// Steam 库定位 + Wallpaper Engine 安装/配置定位
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WeCleaner
{
    public sealed class SteamLibrary
    {
        public string Root;          // D:\Steam
        public string SteamAppsDir;  // D:\Steam\steamapps
        public string AcfPath;       // ...\workshop\appworkshop_431960.acf
        public string ContentDir;    // ...\workshop\content\431960
        public string DownloadsDir;  // ...\workshop\downloads\431960
        public string WeInstallDir;  // ...\common\wallpaper_engine

        public bool HasAcf { get { return File.Exists(AcfPath); } }
        public bool HasContentDir { get { return Directory.Exists(ContentDir); } }
        public bool HasDownloadsDir { get { return Directory.Exists(DownloadsDir); } }
        public bool HasWe { get { return Directory.Exists(WeInstallDir); } }

        public override string ToString()
        {
            return Root;
        }
    }

    public static class SteamLocator
    {
        public const string WeAppId = "431960";
        public const string WeFolderName = "wallpaper_engine";

        /// <summary>候选 Steam 根目录，按可信度排序，已去重。</summary>
        public static List<string> CandidateRoots()
        {
            List<string> list = new List<string>();
            List<string> hints = new List<string>();

            hints.Add(ReadRegString(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
            hints.Add(ReadRegString(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            hints.Add(ReadRegString(RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"));

            for (int i = 0; i < hints.Count; i++)
            {
                if (!string.IsNullOrEmpty(hints[i])) AddUnique(list, hints[i].Replace('/', '\\'));
            }

            string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            AddUnique(list, Path.Combine(pf86 ?? @"C:\Program Files (x86)", "Steam"));
            AddUnique(list, Path.Combine(pf ?? @"C:\Program Files", "Steam"));
            AddUnique(list, @"C:\Steam");
            AddUnique(list, @"D:\Steam");
            AddUnique(list, @"E:\Steam");

            // 从每个固定磁盘根探测 Steam / SteamLibrary
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    try
                    {
                        if (drives[i].DriveType != DriveType.Fixed) continue;
                        string r = drives[i].RootDirectory.FullName;
                        AddUnique(list, Path.Combine(r, "Steam"));
                        AddUnique(list, Path.Combine(r, "SteamLibrary"));
                        AddUnique(list, Path.Combine(r, "Games", "Steam"));
                    }
                    catch { }
                }
            }
            catch { }

            List<string> valid = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                if (LooksLikeSteamRoot(list[i])) valid.Add(list[i]);
            }
            return valid;
        }

        private static bool LooksLikeSteamRoot(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                if (File.Exists(Path.Combine(dir, "steamapps", "libraryfolders.vdf"))) return true;
                if (Directory.Exists(Path.Combine(dir, "steamapps", "workshop"))) return true;
                if (File.Exists(Path.Combine(dir, "steam.exe"))) return true;
                if (File.Exists(Path.Combine(dir, "steamapps", "libraryfolders.vdf"))) return true;
                return false;
            }
            catch { return false; }
        }

        private static void AddUnique(List<string> list, string v)
        {
            if (string.IsNullOrEmpty(v)) return;
            string n = NormalizeRoot(v);
            if (n == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(NormalizeRoot(list[i]), n, StringComparison.OrdinalIgnoreCase)) return;
            }
            list.Add(v);
        }

        private static string NormalizeRoot(string p)
        {
            try
            {
                string full = Path.GetFullPath(p).TrimEnd('\\', '/');
                return full.ToLowerInvariant();
            }
            catch { return null; }
        }

        private static string ReadRegString(RegistryHive hive, string subKey, string name)
        {
            try
            {
                RegistryKey baseKey;
                if (hive == RegistryHive.CurrentUser) baseKey = Registry.CurrentUser;
                else if (hive == RegistryHive.LocalMachine)
                {
                    // 32 位视图允许读取 WOW6432Node 之外的项
                    baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                }
                else baseKey = Registry.LocalMachine;

                using (baseKey)
                using (RegistryKey k = baseKey.OpenSubKey(subKey, false))
                {
                    if (k == null) return null;
                    object o = k.GetValue(name);
                    return o == null ? null : o.ToString();
                }
            }
            catch { return null; }
        }

        /// <summary>枚举所有包含 appworkshop_431960.acf 或 content\431960 的库。</summary>
        public static List<SteamLibrary> Libraries()
        {
            List<SteamLibrary> libs = new List<SteamLibrary>();
            List<string> seen = new List<string>();

            List<string> roots = CandidateRoots();
            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                AddLibrary(libs, seen, root);

                // 展开 libraryfolders.vdf 里登记的其他库
                List<string> extra = ParseLibraryFolders(root);
                for (int j = 0; j < extra.Count; j++)
                {
                    AddLibrary(libs, seen, extra[j]);
                }
            }
            return libs;
        }

        private static void AddLibrary(List<SteamLibrary> libs, List<string> seen, string root)
        {
            string norm = NormalizeRoot(root);
            if (norm == null) return;
            for (int i = 0; i < seen.Count; i++)
            {
                if (seen[i] == norm) return;
            }
            seen.Add(norm);

            string steamapps = Path.Combine(root, "steamapps");
            SteamLibrary lib = new SteamLibrary();
            lib.Root = root;
            lib.SteamAppsDir = steamapps;
            lib.AcfPath = Path.Combine(steamapps, "workshop", "appworkshop_" + WeAppId + ".acf");
            lib.ContentDir = Path.Combine(steamapps, "workshop", "content", WeAppId);
            lib.DownloadsDir = Path.Combine(steamapps, "workshop", "downloads", WeAppId);
            lib.WeInstallDir = Path.Combine(steamapps, "common", WeFolderName);

            // 只有确实存在 workshop 数据或 WE 安装的库才算数
            if (lib.HasAcf || lib.HasContentDir || lib.HasWe) libs.Add(lib);
        }

        private static List<string> ParseLibraryFolders(string steamRoot)
        {
            List<string> result = new List<string>();
            string[] candidates = new string[]
            {
                Path.Combine(steamRoot, "config", "libraryfolders.vdf"),
                Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf")
            };

            for (int c = 0; c < candidates.Length; c++)
            {
                string p = candidates[c];
                if (!File.Exists(p)) continue;
                KvNode root;
                try { root = Kv.ParseFile(p); }
                catch { continue; }

                KvNode container = root.First("libraryfolders");
                if (container == null) container = root.First("LibraryFolders");
                if (container == null) continue;

                for (int i = 0; i < container.Children.Count; i++)
                {
                    KvNode n = container.Children[i];
                    if (n.IsBlock())
                    {
                        string path = n.Str("path");
                        if (!string.IsNullOrEmpty(path)) result.Add(path.Replace('/', '\\'));
                    }
                    else if (n.Value != null)
                    {
                        // 旧格式："1"  "E:\\SteamLibrary"
                        string v = n.Value.Replace('/', '\\');
                        if (v.Length > 2 && v[1] == ':' && Directory.Exists(v)) result.Add(v);
                    }
                }
            }
            return result;
        }

        /// <summary>寻找 Wallpaper Engine 安装目录（多库中任意一个）。</summary>
        public static string FindWeInstallDir(List<SteamLibrary> libs)
        {
            for (int i = 0; i < libs.Count; i++)
            {
                if (libs[i].HasWe) return libs[i].WeInstallDir;
            }
            // 回退：注册表
            string weRoot = ReadRegString(RegistryHive.CurrentUser, @"Software\Wallpaper Engine", null);
            return null;
        }
    }
}
