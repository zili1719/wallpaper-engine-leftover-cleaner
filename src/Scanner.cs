// 孤儿壁纸判定引擎：ACF 四重信号 × Wallpaper Engine 引用保护
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WeCleaner
{
    public enum OrphanLevel
    {
        Protected = 0,        // 当前 WE config.json 仍在引用 / 用户白名单
        Subscribed = 1,       // 正常订阅，保留
        FavoriteHint = 2,     // 未订阅但在 WE 收藏夹里，默认不勾选
        BackupHint = 3,       // 仅出现在 WE 历史配置备份里，默认不勾选
        Medium = 4,           // installed+details 都在，但 subscribedby 缺失
        Strong = 5,           // installed 有、details 无 —— 取消订阅后的典型残态
        Critical = 6,         // 磁盘有目录，ACF 完全无记录
        DownloadFragment = 7, // downloads 里的下载残片
        MissingOnDisk = 8     // ACF 有记录但目录不存在（索引脏，不占空间）
    }

    public sealed class WallpaperItem
    {
        public string Id;
        public string LibraryRoot;
        public string ContentPath;
        public bool ContentExists;

        public long Bytes;
        public int FileCount;
        public DateTime DirWriteUtc = DateTime.MinValue;

        public string Title;
        public string Type;
        public string PreviewPath;
        public string Author;

        // ACF 信号
        public bool InInstalled;
        public bool InDetails;
        public bool HasSubscribedBy;
        public string SubscribedBy;
        public long AcfSize;
        public long TimeUpdated;
        public long TimeTouched;
        public string Manifest;

        // WE 信号
        public List<string> WeRefs = new List<string>();
        public bool InWeFavorites;

        public OrphanLevel Level = OrphanLevel.Subscribed;
        public string Reason;

        public bool Deletable
        {
            get
            {
                return Level == OrphanLevel.Medium
                    || Level == OrphanLevel.Strong
                    || Level == OrphanLevel.Critical
                    || Level == OrphanLevel.DownloadFragment;
            }
        }

        public string LevelText
        {
            get
            {
                switch (Level)
                {
                    case OrphanLevel.Protected: return "保护";
                    case OrphanLevel.Subscribed: return "订阅中";
                    case OrphanLevel.FavoriteHint: return "收藏夹";
                    case OrphanLevel.BackupHint: return "历史引用";
                    case OrphanLevel.Medium: return "可疑残片";
                    case OrphanLevel.Strong: return "已退订";
                    case OrphanLevel.Critical: return "无主目录";
                    case OrphanLevel.DownloadFragment: return "下载残片";
                    case OrphanLevel.MissingOnDisk: return "仅索引";
                    default: return "?";
                }
            }
        }
    }

    public sealed class ScanOptions
    {
        public bool IncludeDownloads = true;
        public bool ReadTitles = true;
        public List<string> ExtraLibraries = new List<string>();
        public string WeInstallDir;
    }

    public sealed class ScanReport
    {
        public DateTime ScannedAtUtc = DateTime.UtcNow;
        public List<WallpaperItem> Items = new List<WallpaperItem>();
        public List<SteamLibrary> Libraries = new List<SteamLibrary>();
        public List<string> Warnings = new List<string>();
        public List<string> WeConfigPaths = new List<string>();

        public long DeletableBytes
        {
            get
            {
                long t = 0;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].Deletable) t += Items[i].Bytes;
                }
                return t;
            }
        }

        public long TotalContentBytes
        {
            get
            {
                long t = 0;
                for (int i = 0; i < Items.Count; i++) t += Items[i].Bytes;
                return t;
            }
        }

        public int DeletableCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Items.Count; i++) if (Items[i].Deletable) n++;
                return n;
            }
        }
    }

    public static class Scanner
    {
        private static readonly Regex RxWePath = new Regex(@"431960[\\/]+(\d{4,})", RegexOptions.Compiled);
        private static readonly Regex RxDigits = new Regex(@"^\d{4,}$", RegexOptions.Compiled);

        public static ScanReport Scan(ScanOptions opt)
        {
            if (opt == null) opt = new ScanOptions();
            ScanReport rep = new ScanReport();

            List<SteamLibrary> libs = SteamLocator.Libraries();

            // 追加用户手动指定的库
            for (int i = 0; i < opt.ExtraLibraries.Count; i++)
            {
                string root = opt.ExtraLibraries[i];
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                bool dup = false;
                for (int j = 0; j < libs.Count; j++)
                {
                    if (string.Equals(libs[j].Root, root, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (dup) continue;
                SteamLibrary l = new SteamLibrary();
                l.Root = root;
                l.SteamAppsDir = Path.Combine(root, "steamapps");
                l.AcfPath = Path.Combine(l.SteamAppsDir, "workshop", "appworkshop_" + SteamLocator.WeAppId + ".acf");
                l.ContentDir = Path.Combine(l.SteamAppsDir, "workshop", "content", SteamLocator.WeAppId);
                l.DownloadsDir = Path.Combine(l.SteamAppsDir, "workshop", "downloads", SteamLocator.WeAppId);
                l.WeInstallDir = Path.Combine(l.SteamAppsDir, "common", SteamLocator.WeFolderName);
                libs.Add(l);
            }

            rep.Libraries = libs;
            if (libs.Count == 0)
            {
                rep.Warnings.Add("未找到任何 Steam 库。请确认 Steam 已安装，或手动添加库路径。");
                return rep;
            }

            // 1. 汇总 WE 引用保护集合（当前 config.json = 硬保护；config_backups = 软提示）
            HashSet<string> hardRefs = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> softRefs = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> weFavs = new HashSet<string>(StringComparer.Ordinal);
            List<string> weConfigs = FindWeConfigs(libs, opt.WeInstallDir);
            rep.WeConfigPaths.AddRange(weConfigs);
            for (int i = 0; i < weConfigs.Count; i++)
            {
                bool backup = IsBackupConfig(weConfigs[i]);
                HashSet<string> target = backup ? softRefs : hardRefs;
                try { HarvestWeReferences(weConfigs[i], target, weFavs); }
                catch (Exception ex) { rep.Warnings.Add("读取 WE 配置失败: " + weConfigs[i] + " -> " + ex.Message); }
            }

            HashSet<string> userKeep = UserKeepList.Load();

            // 2. 逐库扫描
            for (int li = 0; li < libs.Count; li++)
            {
                SteamLibrary lib = libs[li];
                Dictionary<string, WallpaperItem> map = new Dictionary<string, WallpaperItem>(StringComparer.Ordinal);

                // 2a. 解析 ACF
                if (lib.HasAcf)
                {
                    try { ReadAcf(lib.AcfPath, map, lib); }
                    catch (Exception ex) { rep.Warnings.Add("解析 ACF 失败: " + lib.AcfPath + " -> " + ex.Message); }
                }

                // 2b. 扫描 content 目录
                if (lib.HasContentDir)
                {
                    string[] dirs;
                    try { dirs = Directory.GetDirectories(lib.ContentDir); }
                    catch (Exception ex)
                    {
                        rep.Warnings.Add("枚举内容目录失败: " + lib.ContentDir + " -> " + ex.Message);
                        dirs = new string[0];
                    }

                    for (int d = 0; d < dirs.Length; d++)
                    {
                        string full = dirs[d];
                        string id = Path.GetFileName(full);
                        if (id.StartsWith(".")) continue;
                        if (!RxDigits.IsMatch(id)) continue;

                        WallpaperItem it;
                        if (!map.TryGetValue(id, out it))
                        {
                            it = new WallpaperItem();
                            it.Id = id;
                            it.LibraryRoot = lib.Root;
                            map[id] = it;
                        }
                        it.ContentPath = full;
                        it.ContentExists = true;
                        MeasureDirectory(it);
                        if (opt.ReadTitles) ReadProjectMeta(it);
                    }
                }

                // 2c. downloads 残片
                if (opt.IncludeDownloads && lib.HasDownloadsDir)
                {
                    string[] frags;
                    try { frags = Directory.GetDirectories(lib.DownloadsDir); }
                    catch { frags = new string[0]; }
                    for (int f = 0; f < frags.Length; f++)
                    {
                        string full = frags[f];
                        string id = Path.GetFileName(full);
                        WallpaperItem it = new WallpaperItem();
                        it.Id = id;
                        it.LibraryRoot = lib.Root;
                        it.ContentPath = full;
                        it.ContentExists = true;
                        it.Level = OrphanLevel.DownloadFragment;
                        it.Reason = "workshop\\downloads 中的未完成下载分片";
                        MeasureDirectory(it);
                        map["dl:" + id] = it;
                    }
                }

                // 2d. 判定
                foreach (KeyValuePair<string, WallpaperItem> kv in map)
                {
                    WallpaperItem it = kv.Value;
                    Classify(it, hardRefs, softRefs, weFavs, userKeep);
                    rep.Items.Add(it);
                }
            }

            // 去重（同一 ID 在多库出现的按路径区分，保留全部，因为它们各占空间）
            rep.Items.Sort(delegate (WallpaperItem a, WallpaperItem b)
            {
                int c = ((int)b.Level).CompareTo((int)a.Level);
                if (c != 0) return c;
                c = b.Bytes.CompareTo(a.Bytes);
                if (c != 0) return c;
                return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
            });

            return rep;
        }

        private static void Classify(WallpaperItem it, HashSet<string> hardRefs, HashSet<string> softRefs, HashSet<string> weFavs, HashSet<string> userKeep)
        {
            if (it.Level == OrphanLevel.DownloadFragment) return;

            if (hardRefs.Contains(it.Id))
            {
                it.WeRefs.Add("config.json 引用");
                it.Level = OrphanLevel.Protected;
                it.Reason = "WE 当前配置仍引用该壁纸";
                return;
            }
            if (userKeep.Contains(it.Id))
            {
                it.Level = OrphanLevel.Protected;
                it.Reason = "在保留白名单内";
                return;
            }
            if (it.HasSubscribedBy)
            {
                it.Level = OrphanLevel.Subscribed;
                it.Reason = "活跃订阅 (subscribedby=" + (it.SubscribedBy ?? "?") + ")";
                return;
            }

            if (weFavs.Contains(it.Id))
            {
                it.InWeFavorites = true;
                it.Level = OrphanLevel.FavoriteHint;
                it.Reason = "已退订，但仍在 WE 收藏夹";
                return;
            }

            if (!it.ContentExists)
            {
                it.Level = OrphanLevel.MissingOnDisk;
                it.Reason = "ACF 有索引、磁盘无目录（不占空间）";
                return;
            }

            if (softRefs.Contains(it.Id))
            {
                it.Level = OrphanLevel.BackupHint;
                it.Reason = "已退订，仅见于 WE 历史配置备份";
                return;
            }

            if (!it.InInstalled && !it.InDetails)
            {
                it.Level = OrphanLevel.Critical;
                it.Reason = "ACF 无任何记录（彻底无主目录）";
                return;
            }
            if (it.InInstalled && !it.InDetails)
            {
                it.Level = OrphanLevel.Strong;
                it.Reason = "Installed 有 / Details 无（取消订阅残留）";
                return;
            }
            if (it.InInstalled && it.InDetails)
            {
                it.Level = OrphanLevel.Medium;
                it.Reason = "两端有索引但缺 subscribedby（疑似订阅丢失）";
                return;
            }
            // details 有，installed 无
            it.Level = OrphanLevel.Medium;
            it.Reason = "仅详情无安装，且未标记订阅";
        }

        public static bool IsBackupConfig(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.IndexOf("config_backups", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---------------- ACF ----------------

        private static void ReadAcf(string acfPath, Dictionary<string, WallpaperItem> map, SteamLibrary lib)
        {
            KvNode root = Kv.ParseFile(acfPath);
            KvNode aw = root.First("AppWorkshop");
            if (aw == null) return;

            KvNode installed = aw.First("WorkshopItemsInstalled");
            if (installed != null)
            {
                for (int i = 0; i < installed.Children.Count; i++)
                {
                    KvNode n = installed.Children[i];
                    if (!RxDigits.IsMatch(n.Key)) continue;
                    WallpaperItem it = GetOrCreate(map, n.Key, lib.Root);
                    it.InInstalled = true;
                    it.AcfSize = n.Long("size", 0);
                    it.Manifest = n.Str("manifest");
                    it.TimeUpdated = n.Long("timeupdated", 0);
                }
            }

            KvNode details = aw.First("WorkshopItemDetails");
            if (details != null)
            {
                for (int i = 0; i < details.Children.Count; i++)
                {
                    KvNode n = details.Children[i];
                    if (!RxDigits.IsMatch(n.Key)) continue;
                    WallpaperItem it = GetOrCreate(map, n.Key, lib.Root);
                    it.InDetails = true;
                    string sb = n.Str("subscribedby");
                    if (!string.IsNullOrEmpty(sb) && sb != "0")
                    {
                        it.HasSubscribedBy = true;
                        it.SubscribedBy = sb;
                    }
                    long tu = n.Long("timetouched", 0);
                    if (tu > it.TimeTouched) it.TimeTouched = tu;
                }
            }
        }

        private static WallpaperItem GetOrCreate(Dictionary<string, WallpaperItem> map, string id, string libRoot)
        {
            WallpaperItem it;
            if (map.TryGetValue(id, out it)) return it;
            it = new WallpaperItem();
            it.Id = id;
            it.LibraryRoot = libRoot;
            map[id] = it;
            return it;
        }

        // ---------------- 目录测量 ----------------

        private static void MeasureDirectory(WallpaperItem it)
        {
            long total = 0;
            int count = 0;
            try
            {
                DirectoryInfo di = new DirectoryInfo(it.ContentPath);
                if (!di.Exists) return;
                it.DirWriteUtc = di.LastWriteTimeUtc;
                foreach (FileInfo f in di.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total += f.Length;
                        count++;
                    }
                    catch { }
                }
            }
            catch { }
            it.Bytes = total;
            it.FileCount = count;
        }

        // ---------------- project.json ----------------

        private static void ReadProjectMeta(WallpaperItem it)
        {
            string pj = FindProjectJson(it.ContentPath);
            if (pj == null) return;
            try
            {
                object o = Json.ParseFile(pj);
                Dictionary<string, object> d = Json.Obj(o);
                if (d == null) return;
                it.Title = Json.GetStr(d, "title", null);
                it.Type = Json.GetStr(d, "type", null);
                string prev = Json.GetStr(d, "preview", null);
                if (prev != null)
                {
                    string pv = Path.Combine(Path.GetDirectoryName(pj), prev.Replace('/', '\\'));
                    if (File.Exists(pv)) it.PreviewPath = pv;
                }
                if (it.Title == null)
                {
                    string desc = Json.GetStr(d, "description", null);
                    if (desc != null && desc.Length > 0 && desc.Length < 80) it.Title = desc;
                }
            }
            catch { }
        }

        private static string FindProjectJson(string dir)
        {
            string a = Path.Combine(dir, "project.json");
            if (File.Exists(a)) return a;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { return null; }

            for (int i = 0; i < subs.Length; i++)
            {
                string b = Path.Combine(subs[i], "project.json");
                if (File.Exists(b)) return b;
            }
            return null;
        }

        // ---------------- WE 配置 ----------------

        public static List<string> FindWeConfigs(List<SteamLibrary> libs, string explicitDir)
        {
            List<string> found = new List<string>();
            List<string> seen = new List<string>();
            List<string> backups = new List<string>();
            List<string> seenBackups = new List<string>();

            if (!string.IsNullOrEmpty(explicitDir))
                Push(found, seen, Path.Combine(explicitDir, "config.json"));

            for (int i = 0; i < libs.Count; i++)
            {
                if (!libs[i].HasWe) continue;
                Push(found, seen, Path.Combine(libs[i].WeInstallDir, "config.json"));

                // config_backups 里的历史配置只作为软提示来源
                string bdir = Path.Combine(libs[i].WeInstallDir, "config_backups");
                if (Directory.Exists(bdir))
                {
                    try
                    {
                        string[] files = Directory.GetFiles(bdir, "*.json");
                        Array.Sort(files);
                        for (int f = files.Length - 1; f >= 0 && backups.Count < 8; f--)
                            Push(backups, seenBackups, files[f]);
                    }
                    catch { }
                }
            }

            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Push(found, seen, Path.Combine(appdata, "Wallpaper Engine", "config.json"));

            string localappdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Push(found, seen, Path.Combine(localappdata, "Wallpaper Engine", "config.json"));

            // 当前配置在前，历史备份在后
            found.AddRange(backups);
            return found;
        }

        private static void Push(List<string> found, List<string> seen, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) return;
            string n = path.ToLowerInvariant();
            for (int i = 0; i < seen.Count; i++) if (seen[i] == n) return;
            seen.Add(n);
            found.Add(path);
        }

        public static void HarvestWeReferences(string configPath, HashSet<string> refs, HashSet<string> favorites)
        {
            object root = Json.ParseFile(configPath);
            WalkJson(root, refs, null);

            // selectedwallpapers：逗号分隔的 ID 列表
            Dictionary<string, object> top = Json.Obj(root);
            if (top != null)
            {
                object sel;
                if (top.TryGetValue("selectedwallpapers", out sel))
                {
                    string s = sel as string;
                    if (s != null)
                    {
                        string[] parts = s.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (RxDigits.IsMatch(parts[i])) refs.Add(parts[i]);
                        }
                    }
                }

                // explore.favorites 作为软保护
                object user;
                if (top.TryGetValue("user", out user))
                {
                    Dictionary<string, object> u = Json.Obj(user);
                    if (u != null)
                    {
                        object browser;
                        if (u.TryGetValue("browser", out browser))
                        {
                            Dictionary<string, object> b = Json.Obj(browser);
                            if (b != null)
                            {
                                object explore;
                                if (b.TryGetValue("explore", out explore))
                                {
                                    Dictionary<string, object> e = Json.Obj(explore);
                                    if (e != null)
                                    {
                                        object fav;
                                        if (e.TryGetValue("favorites", out fav))
                                        {
                                            List<object> arr = Json.Arr(fav);
                                            if (arr != null)
                                            {
                                                for (int i = 0; i < arr.Count; i++)
                                                {
                                                    string v = arr[i] as string;
                                                    if (v != null && RxDigits.IsMatch(v)) favorites.Add(v);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void WalkJson(object node, HashSet<string> refs, HashSet<string> favorites)
        {
            if (node == null) return;
            string s = node as string;
            if (s != null)
            {
                MatchCollection ms = RxWePath.Matches(s);
                for (int i = 0; i < ms.Count; i++) refs.Add(ms[i].Groups[1].Value);
                return;
            }
            Dictionary<string, object> d = node as Dictionary<string, object>;
            if (d != null)
            {
                foreach (KeyValuePair<string, object> kv in d)
                {
                    MatchCollection ms = RxWePath.Matches(kv.Key);
                    for (int i = 0; i < ms.Count; i++) refs.Add(ms[i].Groups[1].Value);
                    WalkJson(kv.Value, refs, favorites);
                }
                return;
            }
            List<object> a = node as List<object>;
            if (a != null)
            {
                for (int i = 0; i < a.Count; i++) WalkJson(a[i], refs, favorites);
            }
        }

        // ---------------- 工具 ----------------

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            double v = bytes;
            string[] units = new string[] { "KB", "MB", "GB", "TB" };
            int u = -1;
            do
            {
                v /= 1024.0;
                u++;
            } while (v >= 1024.0 && u < units.Length - 1);
            return v.ToString(v >= 100 ? "F0" : (v >= 10 ? "F1" : "F2"), CultureInfo.InvariantCulture) + " " + units[u];
        }
    }

    /// <summary>用户手动维护的保留白名单（JSON 中保存 workshop ID 数组）。</summary>
    public static class UserKeepList
    {
        public static string PathFile
        {
            get
            {
                return Path.Combine(AppPaths.DataDir, "keep.json");
            }
        }

        public static HashSet<string> Load()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            string p = PathFile;
            if (!File.Exists(p)) return set;
            try
            {
                object o = Json.ParseFile(p);
                List<object> arr = Json.Arr(o);
                if (arr == null)
                {
                    Dictionary<string, object> d = Json.Obj(o);
                    if (d != null)
                    {
                        object ids;
                        if (d.TryGetValue("ids", out ids)) arr = Json.Arr(ids);
                    }
                }
                if (arr != null)
                {
                    for (int i = 0; i < arr.Count; i++)
                    {
                        string s = arr[i] as string;
                        if (!string.IsNullOrEmpty(s)) set.Add(s.Trim());
                    }
                }
            }
            catch { }
            return set;
        }

        public static void Save(HashSet<string> set)
        {
            List<object> arr = new List<object>();
            foreach (string s in set) arr.Add(s);
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["ids"] = arr;
            d["updated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            try { File.WriteAllText(PathFile, Json.Serialize(d), new UTF8Encoding(false)); }
            catch { }
        }
    }

    public static class AppPaths
    {
        private const string FolderName = "WallpaperEngineLeftoverCleaner";
        private static string _dataDir;
        private static bool _resolved;
        public static string ResolveError;

        private static string[] Candidates()
        {
            List<string> list = new List<string>();

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local)) list.Add(Path.Combine(local, FolderName));

            string exeDir = null;
            try { exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { }
            if (!string.IsNullOrEmpty(exeDir)) list.Add(Path.Combine(exeDir, "data"));

            string roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(roam)) list.Add(Path.Combine(roam, FolderName));

            string tmp = Path.GetTempPath();
            if (!string.IsNullOrEmpty(tmp)) list.Add(Path.Combine(tmp, FolderName));

            return list.ToArray();
        }

        public static string DataDir
        {
            get
            {
                if (_resolved) return _dataDir;
                _resolved = true;
                string[] cands = Candidates();
                for (int i = 0; i < cands.Length; i++)
                {
                    string d = cands[i];
                    try
                    {
                        if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                        string probe = UniqueProbePath(d);
                        File.WriteAllText(probe, "ok");
                        try { File.Delete(probe); }
                        catch { }
                        _dataDir = d;
                        return _dataDir;
                    }
                    catch (Exception ex)
                    {
                        if (ResolveError == null) ResolveError = d + " -> " + ex.Message;
                    }
                }
                _dataDir = cands.Length > 0 ? cands[0] : Path.GetTempPath();
                return _dataDir;
            }
        }

        public static bool IsWritable
        {
            get
            {
                try
                {
                    string d = DataDir;
                    string probe = UniqueProbePath(d);
                    File.WriteAllText(probe, "ok");
                    try { File.Delete(probe); }
                    catch { }
                    return true;
                }
                catch { return false; }
            }
        }

        /// <summary>每次探测都用唯一文件名，避免多进程并发时互相抢同一个探针文件而误判不可写。</summary>
        private static string UniqueProbePath(string dir)
        {
            string name = ".probe-" + System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            return Path.Combine(dir, name);
        }

        public static string QuarantineRoot
        {
            get
            {
                string dir = Path.Combine(DataDir, "quarantine");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
                catch { }
                return dir;
            }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(DataDir, "settings.json"); }
        }
    }
}
