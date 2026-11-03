// 入口与命令行
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WeCleaner
{
    internal static class ConsoleHelper
    {
        private static bool _attached;

        public static void Attach()
        {
            if (_attached) return;
            _attached = true;
            try
            {
                if (!NativeMethods.AttachConsole(-1))
                {
                    NativeMethods.AllocConsole();
                }
                NativeMethods.SetConsoleOutputCP(65001);
                NativeMethods.SetConsoleCP(65001);
                Stream stdout = Console.OpenStandardOutput();
                StreamWriter w = new StreamWriter(stdout, new UTF8Encoding(false));
                w.AutoFlush = true;
                Console.SetOut(w);
            }
            catch { }
        }

        public static void Write(string s)
        {
            try { Console.Out.Write(s); }
            catch { }
        }

        public static void WriteLine(string s)
        {
            try { Console.Out.WriteLine(s); }
            catch { }
        }

        /// <summary>按终端显示宽度补齐（CJK 算 2 列）。</summary>
        public static string PadDisplay(string s, int width)
        {
            if (s == null) s = "";
            int w = DisplayWidth(s);
            if (w >= width) return s;
            return s + new string(' ', width - w);
        }

        public static int DisplayWidth(string s)
        {
            if (s == null) return 0;
            int w = 0;
            for (int i = 0; i < s.Length; i++) w += CharWidth(s[i]);
            return w;
        }

        public static int CharWidth(char c)
        {
            if (c < 0x80) return 1;
            if (IsWide(c)) return 2;
            return 1;
        }

        private static bool IsWide(char c)
        {
            if (c >= 0x1100 && c <= 0x115F) return true;
            if (c >= 0x2E80 && c <= 0xA4CF) return true;
            if (c >= 0xA960 && c <= 0xA97F) return true;
            if (c >= 0xAC00 && c <= 0xD7A3) return true;
            if (c >= 0xF900 && c <= 0xFAFF) return true;
            if (c >= 0xFE10 && c <= 0xFE19) return true;
            if (c >= 0xFE30 && c <= 0xFE6F) return true;
            if (c >= 0xFF00 && c <= 0xFF60) return true;
            if (c >= 0xFFE0 && c <= 0xFFE6) return true;
            return false;
        }

        public static void Rule(int width)
        {
            WriteLine(new string('-', width));
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length == 0)
            {
                RunGui();
                return 0;
            }

            string cmd = args[0].ToLowerInvariant();
            if (cmd == "gui" || cmd == "--gui")
            {
                RunGui();
                return 0;
            }
            if (cmd == "quarantine-ui")
            {
                Application.Run(new QuarantineForm());
                return 0;
            }
            if (cmd == "tray")
            {
                MainForm owner = new MainForm();
                TrayHost.Show(owner);
                Application.Run(new ApplicationContext());
                return 0;
            }

            ConsoleHelper.Attach();
            try
            {
                return RunCli(args);
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteLine("执行失败: " + ex.Message);
                ConsoleHelper.WriteLine(ex.StackTrace);
                return 2;
            }
        }

        private static void RunGui()
        {
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                LogCrash(e.ExceptionObject as Exception);
            };
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                LogCrash(e.Exception);
                MessageBox.Show("程序发生异常：\n" + e.Exception.Message + "\n\n详情已写入:\n"
                    + Path.Combine(AppPaths.DataDir, "crash.log"), "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
        }

        private static void LogCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string p = Path.Combine(AppPaths.DataDir, "crash.log");
                File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine
                    + ex.ToString() + Environment.NewLine + Environment.NewLine, new UTF8Encoding(true));
            }
            catch { }
        }

        // ---------------- CLI ----------------

        private static int RunCli(string[] args)
        {
            string cmd = args[0].ToLowerInvariant();
            switch (cmd)
            {
                case "scan": return CmdScan(args);
                case "quarantine": return CmdQuarantine(args);
                case "batches": return CmdBatches();
                case "restore": return CmdRestore(args);
                case "purge": return CmdPurge(args);
                case "keep": return CmdKeep(args);
                case "paths": return CmdPaths(args);
                case "tray": return CmdTray();
                case "quarantine-ui": return CmdQuarantineUi();
                case "shellopen": return CmdShellOpen(args);
                case "help":
                case "-h":
                case "/?":
                    Help();
                    return 0;
                default:
                    ConsoleHelper.WriteLine("未知命令: " + args[0]);
                    Help();
                    return 1;
            }
        }

        private static void Help()
        {
            ConsoleHelper.WriteLine("");
            ConsoleHelper.WriteLine("Wallpaper Engine 壁纸残留清理器  v1.0");
            ConsoleHelper.WriteLine("清理取消订阅后仍残留在磁盘上的 Workshop 壁纸文件。");
            ConsoleHelper.WriteLine("");
            ConsoleHelper.WriteLine("用法:");
            ConsoleHelper.WriteLine("  WeCleaner.exe                        启动图形界面");
            ConsoleHelper.WriteLine("  WeCleaner.exe scan [--json] [--out FILE] [--lib DIR]");
            ConsoleHelper.WriteLine("                                       扫描并列出所有壁纸及判定结果");
            ConsoleHelper.WriteLine("  WeCleaner.exe quarantine --all [-y] [--dry-run]");
            ConsoleHelper.WriteLine("  WeCleaner.exe quarantine --id 123,456 [-y]");
            ConsoleHelper.WriteLine("                                       把已退订残片移入隔离区（可恢复）");
            ConsoleHelper.WriteLine("  WeCleaner.exe batches                列出隔离批次");
            ConsoleHelper.WriteLine("  WeCleaner.exe restore [--batch latest|名称]");
            ConsoleHelper.WriteLine("                                       恢复隔离批次");
            ConsoleHelper.WriteLine("  WeCleaner.exe purge [--batch latest|名称] [-y]");
            ConsoleHelper.WriteLine("                                       永久删除隔离批次");
            ConsoleHelper.WriteLine("  WeCleaner.exe keep add 123 456       加入保留白名单");
            ConsoleHelper.WriteLine("  WeCleaner.exe keep list              查看白名单");
            ConsoleHelper.WriteLine("  WeCleaner.exe keep remove 123        移出白名单");
            ConsoleHelper.WriteLine("  WeCleaner.exe paths                  显示探测到的 Steam 库与 WE 配置");
            ConsoleHelper.WriteLine("  WeCleaner.exe tray                   仅启动托盘监视，不进主窗口");
            ConsoleHelper.WriteLine("");
            ConsoleHelper.WriteLine("判定分级:");
            ConsoleHelper.WriteLine("  订阅中    ACF 中 subscribedby 有效 —— 保留");
            ConsoleHelper.WriteLine("  保护      当前 WE config.json 仍在引用，或在保留白名单里 —— 保留");
            ConsoleHelper.WriteLine("  收藏夹    已退订但在 WE 收藏里 —— 默认不清理");
            ConsoleHelper.WriteLine("  历史引用  只在 WE 的历史配置备份中出现过 —— 默认不清理");
            ConsoleHelper.WriteLine("  已退订    Installed 有、Details 无 —— 典型取消订阅残片，可安全清理");
            ConsoleHelper.WriteLine("  无主目录  磁盘有目录、ACF 完全无记录 —— 彻底无主");
            ConsoleHelper.WriteLine("  可疑残片  两端都有索引但缺 subscribedby —— 需人工确认");
            ConsoleHelper.WriteLine("  下载残片  workshop\\downloads 中的未完成分片");
            ConsoleHelper.WriteLine("");
        }

        private static ScanOptions BuildOptions(string[] args)
        {
            AppSettings s = AppSettings.Load();
            ScanOptions opt = new ScanOptions();
            opt.IncludeDownloads = s.IncludeDownloads;
            opt.ReadTitles = true;
            opt.WeInstallDir = s.WeInstallDir;
            opt.ExtraLibraries.AddRange(s.ExtraLibraries);

            // --lib 可指定任意库根（含 steamapps 的那一层），便于测试与手动纳入
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--lib", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    string v = args[i + 1];
                    if (!opt.ExtraLibraries.Contains(v)) opt.ExtraLibraries.Add(v);
                }
            }
            return opt;
        }

        private static int CmdPaths(string[] args)
        {
            List<SteamLibrary> libs = SteamLocator.Libraries();
            ConsoleHelper.WriteLine("Steam 库 (" + libs.Count + "):");
            for (int i = 0; i < libs.Count; i++)
            {
                SteamLibrary l = libs[i];
                ConsoleHelper.WriteLine("  " + l.Root);
                ConsoleHelper.WriteLine("    acf        " + (l.HasAcf ? l.AcfPath : "(无)"));
                ConsoleHelper.WriteLine("    content    " + (l.HasContentDir ? l.ContentDir : "(无)"));
                ConsoleHelper.WriteLine("    downloads  " + (l.HasDownloadsDir ? l.DownloadsDir : "(无)"));
                ConsoleHelper.WriteLine("    WE 安装     " + (l.HasWe ? l.WeInstallDir : "(无)"));
            }

            List<string> cfgs = Scanner.FindWeConfigs(libs, null);
            ConsoleHelper.WriteLine("");
            ConsoleHelper.WriteLine("Wallpaper Engine 配置 (" + cfgs.Count + "):");
            for (int i = 0; i < cfgs.Count; i++) ConsoleHelper.WriteLine("  " + cfgs[i]);
            ConsoleHelper.WriteLine("");
            ConsoleHelper.WriteLine("数据目录: " + AppPaths.DataDir);
            if (!AppPaths.IsWritable)
            {
                ConsoleHelper.WriteLine("警告: 数据目录不可写，隔离功能将无法使用。");
                if (AppPaths.ResolveError != null) ConsoleHelper.WriteLine("      " + AppPaths.ResolveError);
            }
            return 0;
        }

        private static int CmdScan(string[] args)
        {
            bool json = HasFlag(args, "--json");
            string outFile = GetValue(args, "--out", null);

            ScanReport rep = Scanner.Scan(BuildOptions(args));

            if (json)
            {
                string text = ReportToJson(rep);
                if (outFile != null) File.WriteAllText(outFile, text, new UTF8Encoding(false));
                ConsoleHelper.WriteLine(text);
                return 0;
            }

            ConsoleHelper.WriteLine("");
            ConsoleHelper.WriteLine("Steam 库 : " + rep.Libraries.Count + " 个");
            for (int i = 0; i < rep.Libraries.Count; i++) ConsoleHelper.WriteLine("           " + rep.Libraries[i].Root);
            for (int i = 0; i < rep.WeConfigPaths.Count; i++)
                ConsoleHelper.WriteLine("WE 配置  : " + rep.WeConfigPaths[i]);
            ConsoleHelper.WriteLine("");

            ConsoleHelper.Rule(120);
            ConsoleHelper.WriteLine(ConsoleHelper.PadDisplay("状态", 11)
                + ConsoleHelper.PadDisplay("Workshop ID", 14)
                + ConsoleHelper.PadDisplay("占用", 12)
                + ConsoleHelper.PadDisplay("标题", 46)
                + "判定依据");
            ConsoleHelper.Rule(120);

            int shown = 0;
            for (int i = 0; i < rep.Items.Count; i++)
            {
                WallpaperItem it = rep.Items[i];
                if (it.Level == OrphanLevel.MissingOnDisk) continue;
                ConsoleHelper.WriteLine(ConsoleHelper.PadDisplay(it.LevelText, 11)
                    + ConsoleHelper.PadDisplay(it.Id, 14)
                    + ConsoleHelper.PadDisplay(Scanner.FormatBytes(it.Bytes), 12)
                    + ConsoleHelper.PadDisplay(Truncate(it.Title ?? "(无标题)", 44), 46)
                    + Truncate(it.Reason ?? "", 58));
                shown++;
            }
            ConsoleHelper.Rule(120);
            ConsoleHelper.WriteLine("共 " + shown + " 项，全部占用 " + Scanner.FormatBytes(rep.TotalContentBytes));
            ConsoleHelper.WriteLine("可清理 " + rep.DeletableCount + " 项，预计释放 " + Scanner.FormatBytes(rep.DeletableBytes));
            ConsoleHelper.WriteLine("");
            ConsoleHelper.WriteLine("分级汇总:");

            OrphanLevel[] levels = new OrphanLevel[]
            {
                OrphanLevel.Subscribed, OrphanLevel.Protected, OrphanLevel.FavoriteHint, OrphanLevel.BackupHint,
                OrphanLevel.Medium, OrphanLevel.Strong, OrphanLevel.Critical, OrphanLevel.DownloadFragment
            };
            for (int lv = 0; lv < levels.Length; lv++)
            {
                int cnt = 0;
                long by = 0;
                for (int i = 0; i < rep.Items.Count; i++)
                {
                    if (rep.Items[i].Level != levels[lv]) continue;
                    cnt++;
                    by += rep.Items[i].Bytes;
                }
                if (cnt == 0) continue;
                WallpaperItem sample = new WallpaperItem();
                sample.Level = levels[lv];
                ConsoleHelper.WriteLine("  " + ConsoleHelper.PadDisplay(sample.LevelText, 11)
                    + ConsoleHelper.PadDisplay(cnt + " 项", 10)
                    + Scanner.FormatBytes(by));
            }

            if (rep.Warnings.Count > 0)
            {
                ConsoleHelper.WriteLine("");
                for (int i = 0; i < rep.Warnings.Count; i++)
                    ConsoleHelper.WriteLine("警告: " + rep.Warnings[i]);
            }
            ConsoleHelper.WriteLine("");

            if (outFile != null)
            {
                File.WriteAllText(outFile, ReportToJson(rep), new UTF8Encoding(false));
                ConsoleHelper.WriteLine("JSON 报告已写入: " + outFile);
            }
            return 0;
        }

        private static string Truncate(string s, int maxWidth)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            int w = 0;
            int i = 0;
            for (; i < s.Length; i++)
            {
                int cw = ConsoleHelper.CharWidth(s[i]);
                if (w + cw > maxWidth - 1) break;
                w += cw;
            }
            if (i >= s.Length) return s;
            return s.Substring(0, i) + "…";
        }

        private static string ReportToJson(ScanReport rep)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["tool"] = "WallpaperEngineLeftoverCleaner";
            root["version"] = 1;
            root["scannedAt"] = rep.ScannedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            root["deletableCount"] = (double)rep.DeletableCount;
            root["deletableBytes"] = (double)rep.DeletableBytes;
            root["totalBytes"] = (double)rep.TotalContentBytes;

            List<object> libList = new List<object>();
            for (int i = 0; i < rep.Libraries.Count; i++)
            {
                Dictionary<string, object> l = new Dictionary<string, object>();
                l["root"] = rep.Libraries[i].Root;
                l["acf"] = rep.Libraries[i].AcfPath;
                l["content"] = rep.Libraries[i].ContentDir;
                libList.Add(l);
            }
            root["libraries"] = libList;

            List<object> weCfgs = new List<object>();
            for (int i = 0; i < rep.WeConfigPaths.Count; i++) weCfgs.Add(rep.WeConfigPaths[i]);
            root["weConfigs"] = weCfgs;

            List<object> items = new List<object>();
            for (int i = 0; i < rep.Items.Count; i++)
            {
                WallpaperItem it = rep.Items[i];
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["id"] = it.Id;
                o["title"] = it.Title;
                o["type"] = it.Type;
                o["level"] = it.Level.ToString();
                o["levelText"] = it.LevelText;
                o["reason"] = it.Reason;
                o["deletable"] = it.Deletable;
                o["bytes"] = (double)it.Bytes;
                o["files"] = (double)it.FileCount;
                o["path"] = it.ContentPath;
                o["library"] = it.LibraryRoot;
                o["inInstalled"] = it.InInstalled;
                o["inDetails"] = it.InDetails;
                o["hasSubscribedBy"] = it.HasSubscribedBy;
                o["subscribedBy"] = it.SubscribedBy;
                o["inWeFavorites"] = it.InWeFavorites;
                items.Add(o);
            }
            root["items"] = items;

            List<object> warns = new List<object>();
            for (int i = 0; i < rep.Warnings.Count; i++) warns.Add(rep.Warnings[i]);
            root["warnings"] = warns;

            return Json.Serialize(root);
        }

        private static int CmdQuarantine(string[] args)
        {
            bool all = HasFlag(args, "--all");
            bool dryRun = HasFlag(args, "--dry-run");
            bool yes = HasFlag(args, "-y") || HasFlag(args, "--yes");
            string idArg = GetValue(args, "--id", null);

            ScanReport rep = Scanner.Scan(BuildOptions(args));
            List<WallpaperItem> targets = new List<WallpaperItem>();

            if (idArg != null)
            {
                string[] ids = idArg.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < ids.Length; i++)
                {
                    WallpaperItem found = null;
                    for (int j = 0; j < rep.Items.Count; j++)
                    {
                        if (rep.Items[j].Id == ids[i]) { found = rep.Items[j]; break; }
                    }
                    if (found == null)
                    {
                        ConsoleHelper.WriteLine("未找到 ID: " + ids[i]);
                        continue;
                    }
                    if (!found.Deletable)
                    {
                        ConsoleHelper.WriteLine("跳过 " + found.Id + " —— " + found.LevelText + "：" + found.Reason);
                        continue;
                    }
                    targets.Add(found);
                }
            }
            else if (all)
            {
                for (int i = 0; i < rep.Items.Count; i++)
                    if (rep.Items[i].Deletable) targets.Add(rep.Items[i]);
            }
            else
            {
                ConsoleHelper.WriteLine("请指定 --all 或 --id <列表>。加 --dry-run 可先看会清理什么。");
                return 1;
            }

            if (targets.Count == 0)
            {
                ConsoleHelper.WriteLine("没有符合条件的项目。");
                return 0;
            }

            long bytes = 0;
            for (int i = 0; i < targets.Count; i++) bytes += targets[i].Bytes;

            ConsoleHelper.WriteLine("");
            ConsoleHelper.WriteLine("将处理 " + targets.Count + " 项 / " + Scanner.FormatBytes(bytes) + ":");
            for (int i = 0; i < targets.Count; i++)
            {
                ConsoleHelper.WriteLine("  [" + targets[i].LevelText + "] " + targets[i].Id
                    + "  " + Scanner.FormatBytes(targets[i].Bytes) + "  " + (targets[i].Title ?? ""));
            }

            if (dryRun)
            {
                ConsoleHelper.WriteLine("");
                ConsoleHelper.WriteLine("(dry-run) 未做任何改动。");
                return 0;
            }

            if (!yes)
            {
                ConsoleHelper.WriteLine("");
                ConsoleHelper.WriteLine("确认移入隔离区？输入 y 回车继续: ");
                string line = null;
                try { line = Console.ReadLine(); }
                catch { }
                if (line == null || line.Trim().ToLowerInvariant() != "y")
                {
                    ConsoleHelper.WriteLine("已取消。");
                    return 0;
                }
            }

            if (Quarantine.IsWeRunning())
                ConsoleHelper.WriteLine("提示: Wallpaper Engine 正在运行，部分文件可能被占用。");

            List<string> errors;
            QuarantineBatch batch = Quarantine.MoveToQuarantine(targets, out errors);
            for (int i = 0; i < errors.Count; i++) ConsoleHelper.WriteLine("失败: " + errors[i]);
            ConsoleHelper.WriteLine("");
            ConsoleHelper.WriteLine("已隔离 " + batch.Entries.Count + " 项 / " + Scanner.FormatBytes(batch.Bytes));
            ConsoleHelper.WriteLine("批次: " + batch.Name);
            ConsoleHelper.WriteLine("恢复: WeCleaner.exe restore --batch " + batch.Name);
            return 0;
        }

        private static int CmdBatches()
        {
            List<QuarantineBatch> list = Quarantine.ListBatches();
            if (list.Count == 0)
            {
                ConsoleHelper.WriteLine("隔离区为空。");
                return 0;
            }

            ConsoleHelper.WriteLine("");
            ConsoleHelper.WriteLine(ConsoleHelper.PadDisplay("批次", 24)
                + ConsoleHelper.PadDisplay("时间", 22)
                + ConsoleHelper.PadDisplay("项目", 8)
                + "占用");
            ConsoleHelper.Rule(70);
            for (int i = 0; i < list.Count; i++)
            {
                ConsoleHelper.WriteLine(ConsoleHelper.PadDisplay(list[i].Name, 24)
                    + ConsoleHelper.PadDisplay(list[i].Created.ToString("yyyy-MM-dd HH:mm:ss"), 22)
                    + ConsoleHelper.PadDisplay(list[i].ActiveCount.ToString(), 8)
                    + Scanner.FormatBytes(list[i].Bytes));
            }
            ConsoleHelper.WriteLine("");
            return 0;
        }

        private static QuarantineBatch ResolveBatch(string name)
        {
            List<QuarantineBatch> list = Quarantine.ListBatches();
            if (list.Count == 0) return null;
            if (string.IsNullOrEmpty(name) || name == "latest") return list[0];
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return list[i];
            }
            return null;
        }

        private static int CmdRestore(string[] args)
        {
            string name = GetValue(args, "--batch", "latest");
            QuarantineBatch b = ResolveBatch(name);
            if (b == null)
            {
                ConsoleHelper.WriteLine("找不到批次: " + name);
                return 1;
            }

            List<string> errors;
            int n = Quarantine.Restore(b, out errors);
            for (int i = 0; i < errors.Count; i++) ConsoleHelper.WriteLine("失败: " + errors[i]);
            ConsoleHelper.WriteLine("已恢复 " + n + " 项到原位置。");
            return 0;
        }

        private static int CmdPurge(string[] args)
        {
            string name = GetValue(args, "--batch", "latest");
            bool yes = HasFlag(args, "-y") || HasFlag(args, "--yes");
            QuarantineBatch b = ResolveBatch(name);
            if (b == null)
            {
                ConsoleHelper.WriteLine("找不到批次: " + name);
                return 1;
            }

            if (!yes)
            {
                ConsoleHelper.WriteLine("将永久删除 " + b.ActiveCount + " 项 / " + Scanner.FormatBytes(b.Bytes) + "。输入 y 确认: ");
                string line = null;
                try { line = Console.ReadLine(); }
                catch { }
                if (line == null || line.Trim().ToLowerInvariant() != "y")
                {
                    ConsoleHelper.WriteLine("已取消。");
                    return 0;
                }
            }

            AppSettings st = AppSettings.Load();
            bool toBin = !HasFlag(args, "--hard") && st.DeleteToRecycleBin;
            string err;
            if (!Quarantine.Purge(b, toBin, out err))
            {
                ConsoleHelper.WriteLine("删除失败: " + err);
                return 1;
            }
            ConsoleHelper.WriteLine("批次 " + b.Name + " 已删除，释放 " + Scanner.FormatBytes(b.Bytes) + "。");
            return 0;
        }

        private static int CmdKeep(string[] args)
        {
            HashSet<string> keep = UserKeepList.Load();
            if (args.Length >= 2 && args[1].ToLowerInvariant() == "list")
            {
                if (keep.Count == 0) ConsoleHelper.WriteLine("白名单为空。");
                foreach (string s in keep) ConsoleHelper.WriteLine("  " + s);
                return 0;
            }
            if (args.Length >= 2 && args[1].ToLowerInvariant() == "add")
            {
                int added = 0;
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i].StartsWith("-")) continue;
                    if (keep.Add(args[i].Trim())) added++;
                }
                UserKeepList.Save(keep);
                ConsoleHelper.WriteLine("已加入 " + added + " 个 ID，白名单共 " + keep.Count + " 项。");
                return 0;
            }
            if (args.Length >= 2 && args[1].ToLowerInvariant() == "remove")
            {
                int removed = 0;
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i].StartsWith("-")) continue;
                    if (keep.Remove(args[i].Trim())) removed++;
                }
                UserKeepList.Save(keep);
                ConsoleHelper.WriteLine("已移除 " + removed + " 个 ID。");
                return 0;
            }
            ConsoleHelper.WriteLine("用法: keep add <id...> | keep remove <id...> | keep list");
            return 1;
        }

        private static int CmdTray()
        {
            MainForm owner = new MainForm();
            TrayHost.Show(owner);
            Application.Run(new ApplicationContext());
            return 0;
        }

        private static int CmdQuarantineUi()
        {
            Application.Run(new QuarantineForm());
            return 0;
        }

        /// <summary>诊断用：测试 Shell 打开路径是否可用。</summary>
        private static int CmdShellOpen(string[] args)
        {
            string target = args.Length > 1 ? args[1] : null;
            if (string.IsNullOrEmpty(target))
            {
                ConsoleHelper.WriteLine("用法: shellopen <path>");
                return 1;
            }
            ConsoleHelper.WriteLine("target: " + target);
            ConsoleHelper.WriteLine("exists: " + (Directory.Exists(target) || File.Exists(target)));
            bool ok = NativeMethods.RevealPath(target);
            ConsoleHelper.WriteLine("RevealPath -> " + ok);
            ConsoleHelper.WriteLine("diag: " + NativeMethods.LastDiag);
            return ok ? 0 : 1;
        }

        // ---------------- 参数工具 ----------------

        private static bool HasFlag(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetValue(string[] args, string key, string def)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return def;
        }
    }
}
