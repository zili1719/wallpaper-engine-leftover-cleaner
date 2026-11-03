// 隔离区：可回滚的移出 / 恢复 / 彻底删除
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace WeCleaner
{
    public sealed class QuarantineEntry
    {
        public string Id;
        public string Title;
        public string SourcePath;
        public string StoredPath;
        public long Bytes;
        public string Level;
        public string Reason;
        public bool Restored;
        public string MovedAt;
    }

    public sealed class QuarantineBatch
    {
        public string Name;
        public string Dir;
        public DateTime Created = DateTime.MinValue;
        public List<QuarantineEntry> Entries = new List<QuarantineEntry>();

        public long Bytes
        {
            get
            {
                long t = 0;
                for (int i = 0; i < Entries.Count; i++) t += Entries[i].Bytes;
                return t;
            }
        }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Entries.Count; i++) if (!Entries[i].Restored) n++;
                return n;
            }
        }
    }

    public static class Quarantine
    {
        public static bool IsWeRunning()
        {
            try
            {
                if (Process.GetProcessesByName("wallpaper32").Length > 0) return true;
                if (Process.GetProcessesByName("wallpaper64").Length > 0) return true;
            }
            catch { }
            return false;
        }

        public static string LongPath(string p)
        {
            if (p == null) return null;
            if (p.StartsWith(@"\\?\")) return p;
            if (p.Length >= 240 && !p.StartsWith(@"\\"))
                return @"\\?\" + p;
            return p;
        }

        /// <summary>把一组壁纸移入新建的隔离批次。</summary>
        public static QuarantineBatch MoveToQuarantine(IList<WallpaperItem> items, out List<string> errors)
        {
            errors = new List<string>();
            QuarantineBatch batch = new QuarantineBatch();
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string dir = Path.Combine(AppPaths.QuarantineRoot, stamp);
            int n = 1;
            while (Directory.Exists(dir))
            {
                dir = Path.Combine(AppPaths.QuarantineRoot, stamp + "-" + n.ToString(CultureInfo.InvariantCulture));
                n++;
            }
            try
            {
                Directory.CreateDirectory(dir);
                Directory.CreateDirectory(Path.Combine(dir, "items"));
            }
            catch (Exception ex)
            {
                errors.Add("无法创建隔离批次目录 " + dir + " -> " + ex.Message);
                batch.Name = "(未创建)";
                batch.Dir = dir;
                return batch;
            }

            batch.Name = Path.GetFileName(dir);
            batch.Dir = dir;
            batch.Created = DateTime.Now;

            for (int i = 0; i < items.Count; i++)
            {
                WallpaperItem it = items[i];
                if (!Directory.Exists(it.ContentPath))
                {
                    errors.Add(it.Id + ": 源目录不存在，跳过");
                    continue;
                }

                string dest = Path.Combine(dir, "items", Sanitize(it.Id));
                try
                {
                    if (Directory.Exists(dest)) dest = dest + "-" + i.ToString(CultureInfo.InvariantCulture);
                    MoveDirectory(it.ContentPath, dest);

                    QuarantineEntry e = new QuarantineEntry();
                    e.Id = it.Id;
                    e.Title = it.Title;
                    e.SourcePath = it.ContentPath;
                    e.StoredPath = dest;
                    e.Bytes = it.Bytes;
                    e.Level = it.LevelText;
                    e.Reason = it.Reason;
                    e.MovedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    batch.Entries.Add(e);
                }
                catch (Exception ex)
                {
                    errors.Add(it.Id + ": " + ex.Message);
                }
            }

            WriteManifest(batch);
            return batch;
        }

        /// <summary>从隔离区恢复到原路径。</summary>
        public static int Restore(QuarantineBatch batch, out List<string> errors)
        {
            errors = new List<string>();
            int ok = 0;
            for (int i = 0; i < batch.Entries.Count; i++)
            {
                QuarantineEntry e = batch.Entries[i];
                if (e.Restored) continue;
                if (!Directory.Exists(e.StoredPath))
                {
                    errors.Add(e.Id + ": 隔离副本已丢失");
                    continue;
                }
                try
                {
                    string parent = Path.GetDirectoryName(e.SourcePath);
                    if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                    if (Directory.Exists(e.SourcePath))
                    {
                        errors.Add(e.Id + ": 原位置已被占用，未能恢复");
                        continue;
                    }
                    MoveDirectory(e.StoredPath, e.SourcePath);
                    e.Restored = true;
                    ok++;
                }
                catch (Exception ex)
                {
                    errors.Add(e.Id + ": " + ex.Message);
                }
            }
            WriteManifest(batch);
            return ok;
        }

        /// <summary>彻底删除一个隔离批次。toRecycleBin=true 时先尝试送入回收站。</summary>
        public static bool Purge(QuarantineBatch batch, bool toRecycleBin, out string error)
        {
            error = null;
            try
            {
                if (!Directory.Exists(batch.Dir)) return true;
                if (toRecycleBin)
                {
                    string rerr;
                    if (RecycleDirectory(batch.Dir, out rerr)) return true;
                    error = rerr; // 回收站失败，回落到直接删除
                }
                DeleteDirectory(batch.Dir);
                // 以磁盘状态为准
                if (Directory.Exists(batch.Dir))
                {
                    if (error == null) error = "目录仍然存在，可能有文件被占用";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Purge(QuarantineBatch batch, out string error)
        {
            return Purge(batch, false, out error);
        }

        public static List<QuarantineBatch> ListBatches()
        {
            List<QuarantineBatch> list = new List<QuarantineBatch>();
            string root = AppPaths.QuarantineRoot;
            if (!Directory.Exists(root)) return list;

            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return list; }

            for (int i = 0; i < dirs.Length; i++)
            {
                QuarantineBatch b = LoadManifest(dirs[i]);
                if (b != null) list.Add(b);
            }
            list.Sort(delegate (QuarantineBatch a, QuarantineBatch c)
            {
                return c.Created.CompareTo(a.Created);
            });
            return list;
        }

        public static QuarantineBatch LoadManifest(string dir)
        {
            string mf = Path.Combine(dir, "manifest.json");
            QuarantineBatch b = new QuarantineBatch();
            b.Name = Path.GetFileName(dir);
            b.Dir = dir;
            try
            {
                b.Created = Directory.GetCreationTime(dir);
            }
            catch { }

            if (!File.Exists(mf)) return b;
            try
            {
                Dictionary<string, object> d = Json.Obj(Json.ParseFile(mf));
                if (d != null)
                {
                    string created = Json.GetStr(d, "created", null);
                    DateTime dt;
                    if (created != null && DateTime.TryParse(created, out dt)) b.Created = dt;
                    object arrObj;
                    if (d.TryGetValue("entries", out arrObj))
                    {
                        List<object> arr = Json.Arr(arrObj);
                        if (arr != null)
                        {
                            for (int i = 0; i < arr.Count; i++)
                            {
                                Dictionary<string, object> ed = Json.Obj(arr[i]);
                                if (ed == null) continue;
                                QuarantineEntry e = new QuarantineEntry();
                                e.Id = Json.GetStr(ed, "id", "");
                                e.Title = Json.GetStr(ed, "title", "");
                                e.SourcePath = Json.GetStr(ed, "src", "");
                                e.StoredPath = Json.GetStr(ed, "dst", "");
                                e.Level = Json.GetStr(ed, "level", "");
                                e.Reason = Json.GetStr(ed, "reason", "");
                                e.MovedAt = Json.GetStr(ed, "moved", "");
                                e.Restored = Json.GetBool(ed, "restored", false);
                                object bs;
                                if (ed.TryGetValue("bytes", out bs) && bs is double) e.Bytes = (long)(double)bs;
                                b.Entries.Add(e);
                            }
                        }
                    }
                }
            }
            catch { }
            return b;
        }

        public static void WriteManifest(QuarantineBatch batch)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["batch"] = batch.Name;
            d["created"] = batch.Created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            d["tool"] = "Wallpaper Engine Leftover Cleaner";
            List<object> arr = new List<object>();
            for (int i = 0; i < batch.Entries.Count; i++)
            {
                QuarantineEntry e = batch.Entries[i];
                Dictionary<string, object> ed = new Dictionary<string, object>();
                ed["id"] = e.Id;
                ed["title"] = e.Title;
                ed["src"] = e.SourcePath;
                ed["dst"] = e.StoredPath;
                ed["bytes"] = e.Bytes;
                ed["level"] = e.Level;
                ed["reason"] = e.Reason;
                ed["moved"] = e.MovedAt;
                ed["restored"] = e.Restored;
                arr.Add(ed);
            }
            d["entries"] = arr;
            try { File.WriteAllText(Path.Combine(batch.Dir, "manifest.json"), Json.Serialize(d), new UTF8Encoding(true)); }
            catch { }
        }

        // ---------------- 文件系统 ----------------

        /// <summary>移动目录。跨卷时先完整复制到 .partial，确认后再删源；任何一步失败都保留源目录。</summary>
        public static void MoveDirectory(string src, string dst)
        {
            try
            {
                Directory.Move(LongPath(src), LongPath(dst));
                return;
            }
            catch (IOException)
            {
                // 跨卷或目标已存在，走复制路径
            }

            CatchReadOnly(src);

            string parent = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(LongPath(parent)))
                Directory.CreateDirectory(LongPath(parent));

            string tmp = dst.TrimEnd('\\') + ".partial";
            if (Directory.Exists(LongPath(tmp))) DeleteDirectory(tmp);

            CopyDirectory(src, tmp);

            // 校验：文件数与总字节一致才替换
            long srcBytes, tmpBytes;
            int srcCount, tmpCount;
            CountTree(src, out srcBytes, out srcCount);
            CountTree(tmp, out tmpBytes, out tmpCount);
            if (srcCount != tmpCount || srcBytes != tmpBytes)
            {
                DeleteDirectory(tmp);
                throw new IOException("复制校验失败（源 " + srcCount + " 个文件 / " + srcBytes + " 字节，目标 "
                    + tmpCount + " 个 / " + tmpBytes + " 字节），已保留原文件。");
            }

            if (Directory.Exists(LongPath(dst))) DeleteDirectory(dst);
            Directory.Move(LongPath(tmp), LongPath(dst));
            DeleteDirectory(src);
        }

        private static void CountTree(string dir, out long bytes, out int count)
        {
            bytes = 0;
            count = 0;
            try
            {
                foreach (FileInfo f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { bytes += f.Length; count++; }
                    catch { }
                }
            }
            catch { }
        }

        private static void CatchReadOnly(string dir)
        {
            try
            {
                foreach (FileInfo f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if ((f.Attributes & FileAttributes.ReadOnly) != 0)
                            f.Attributes = f.Attributes & ~FileAttributes.ReadOnly;
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(LongPath(dst));
            foreach (FileInfo f in new DirectoryInfo(src).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                string rel = f.FullName.Substring(src.Length).TrimStart('\\');
                string target = Path.Combine(dst, rel);
                string parent = Path.GetDirectoryName(target);
                if (!Directory.Exists(LongPath(parent))) Directory.CreateDirectory(LongPath(parent));
                f.CopyTo(LongPath(target), true);
            }
        }

        public static void DeleteDirectory(string dir)
        {
            if (!Directory.Exists(LongPath(dir))) return;
            CatchReadOnly(dir);
            try
            {
                Directory.Delete(LongPath(dir), true);
                return;
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            // 回退：逐文件删除
            foreach (FileInfo f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    f.Attributes = FileAttributes.Normal;
                    f.Delete();
                }
                catch { }
            }
            try { Directory.Delete(LongPath(dir), true); }
            catch { }
        }

        /// <summary>把目录送入回收站。成功以「目录确实消失」为准，不轻信 API 返回值。</summary>
        public static bool RecycleDirectory(string dir, out string error)
        {
            error = null;
            if (!Directory.Exists(LongPath(dir))) return true;
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    dir,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            if (!Directory.Exists(LongPath(dir))) return true;

            // VB API 失败时退回 Shell API 试一次
            try
            {
                if (NativeMethods.SHFileOperationDelete(dir) && !Directory.Exists(LongPath(dir)))
                    return true;
            }
            catch (Exception ex2)
            {
                if (error == null) error = ex2.Message;
            }
            return !Directory.Exists(LongPath(dir));
        }

        private static string Sanitize(string s)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = true;
                for (int j = 0; j < bad.Length; j++) if (bad[j] == c) { ok = false; break; }
                sb.Append(ok ? c : '_');
            }
            string r = sb.ToString();
            return r.Length == 0 ? "item" : r;
        }
    }
}
