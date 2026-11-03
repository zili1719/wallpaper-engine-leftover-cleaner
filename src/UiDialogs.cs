// 对话框：隔离区管理 / 设置 / 托盘常驻监视
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WeCleaner
{
    // =========================================================
    // 隔离区管理
    // =========================================================
    public sealed class QuarantineForm : Form
    {
        private ListView _batches;
        private DataGridView _entries;
        private Button _btnRestore;
        private Button _btnPurge;
        private Button _btnOpen;
        private Button _btnClose;
        private Label _hint;
        private SplitContainer _split;
        private List<QuarantineBatch> _data = new List<QuarantineBatch>();
        private AppSettings _settings;

        public QuarantineForm()
        {
            _settings = AppSettings.Load();
            Build();
            Reload();
        }

        private void Build()
        {
            Text = "隔离区管理";
            Font = new Font("Microsoft YaHei UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(980, 600);
            MinimumSize = new Size(760, 420);
            ShowInTaskbar = false;

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Vertical;

            _batches = new ListView();
            _batches.Dock = DockStyle.Fill;
            _batches.View = View.Details;
            _batches.FullRowSelect = true;
            _batches.MultiSelect = false;
            _batches.HideSelection = false;
            _batches.BorderStyle = BorderStyle.None;
            _batches.Columns.Add("批次时间", 140);
            _batches.Columns.Add("项目", 60, HorizontalAlignment.Right);
            _batches.Columns.Add("占用", 90, HorizontalAlignment.Right);
            _batches.SelectedIndexChanged += delegate { ShowEntries(); };
            _split.Panel1.Controls.Add(_batches);

            _entries = new DataGridView();
            _entries.Dock = DockStyle.Fill;
            _entries.AllowUserToAddRows = false;
            _entries.AllowUserToDeleteRows = false;
            _entries.ReadOnly = true;
            _entries.RowHeadersVisible = false;
            _entries.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _entries.MultiSelect = true;
            _entries.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _entries.BorderStyle = BorderStyle.None;
            _entries.BackgroundColor = Color.White;
            _entries.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            _entries.EnableHeadersVisualStyles = false;
            _entries.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 250);

            AddCol("status", "状态", 60);
            AddCol("id", "Workshop ID", 100);
            AddCol("title", "标题", 240);
            AddCol("size", "占用", 80);
            AddCol("src", "原路径", 320);

            _entries.Columns["title"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _entries.Columns["size"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _entries.Columns["id"].DefaultCellStyle.Font = new Font("Consolas", 9f);
            _entries.DoubleClick += delegate { RestoreSelected(); };
            _split.Panel2.Controls.Add(_entries);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 92;
            bottom.Padding = new Padding(12, 6, 12, 10);

            _hint = new Label();
            _hint.Dock = DockStyle.Top;
            _hint.Height = 34;
            _hint.ForeColor = Color.FromArgb(95, 99, 104);
            _hint.Text = "隔离区里的壁纸随时可以恢复。确认不再需要后，双击批次里的条目或点「彻底删除」。";

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Bottom;
            flow.Height = 46;
            flow.FlowDirection = FlowDirection.LeftToRight;

            _btnRestore = MakeBtn("恢复选中批次", delegate { RestoreSelectedBatch(); });
            _btnOpen = MakeBtn("打开隔离目录", delegate { OpenBatchDir(); });
            _btnPurge = MakeBtn("彻底删除当前批次", delegate { PurgeSelectedBatch(); });
            _btnClose = MakeBtn("关闭", delegate { Close(); });

            flow.Controls.Add(_btnRestore);
            flow.Controls.Add(_btnOpen);
            flow.Controls.Add(_btnPurge);
            flow.Controls.Add(_btnClose);

            bottom.Controls.Add(_hint);
            bottom.Controls.Add(flow);

            Controls.Add(_split);
            Controls.Add(bottom);
            Controls.SetChildIndex(bottom, 0);
            Controls.SetChildIndex(_split, 1);

            Load += delegate
            {
                try { _split.SplitterDistance = 320; } catch { }
            };
        }

        private void AddCol(string name, string header, int width)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = header;
            c.Width = width;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            _entries.Columns.Add(c);
        }

        private Button MakeBtn(string text, EventHandler h)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.Padding = new Padding(8, 3, 8, 3);
            b.FlatStyle = FlatStyle.System;
            b.Click += h;
            return b;
        }

        private void Reload()
        {
            _data = Quarantine.ListBatches();
            _batches.BeginUpdate();
            _batches.Items.Clear();
            for (int i = 0; i < _data.Count; i++)
            {
                QuarantineBatch b = _data[i];
                ListViewItem li = new ListViewItem(b.Created.ToString("yyyy-MM-dd HH:mm"));
                li.SubItems.Add(b.ActiveCount.ToString());
                li.SubItems.Add(Scanner.FormatBytes(b.Bytes));
                li.Tag = b;
                _batches.Items.Add(li);
            }
            _batches.EndUpdate();
            if (_batches.Items.Count > 0) _batches.Items[0].Selected = true;
            else
            {
                _entries.Rows.Clear();
                _hint.Text = "隔离区是空的。所有移动过的壁纸都会出现在这里，并且可以随时恢复。";
            }
        }

        private QuarantineBatch CurrentBatch()
        {
            if (_batches.SelectedItems.Count == 0) return null;
            return _batches.SelectedItems[0].Tag as QuarantineBatch;
        }

        private void ShowEntries()
        {
            _entries.Rows.Clear();
            QuarantineBatch b = CurrentBatch();
            if (b == null) return;
            for (int i = 0; i < b.Entries.Count; i++)
            {
                QuarantineEntry e = b.Entries[i];
                int idx = _entries.Rows.Add();
                DataGridViewRow row = _entries.Rows[idx];
                row.Cells["status"].Value = e.Restored ? "已恢复" : "隔离中";
                row.Cells["id"].Value = e.Id;
                row.Cells["title"].Value = e.Title;
                row.Cells["size"].Value = Scanner.FormatBytes(e.Bytes);
                row.Cells["src"].Value = e.SourcePath;
                row.Tag = e;
            }
        }

        private void RestoreSelected()
        {
            QuarantineBatch b = CurrentBatch();
            if (b == null) return;
            List<string> errors;
            int n = Quarantine.Restore(b, out errors);
            string msg = "已恢复 " + n + " 项。";
            if (errors.Count > 0) msg += "\n\n有问题：\n" + string.Join("\n", errors.ToArray());
            MessageBox.Show(this, msg, "恢复", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Reload();
        }

        private void RestoreSelectedBatch()
        {
            QuarantineBatch b = CurrentBatch();
            if (b == null) { MessageBox.Show(this, "先选中一个批次。", "提示"); return; }
            if (Quarantine.IsWeRunning())
            {
                if (MessageBox.Show(this, "Wallpaper Engine 正在运行，恢复文件可能失败。继续？", "提示",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            }
            RestoreSelected();
        }

        private void PurgeSelectedBatch()
        {
            QuarantineBatch b = CurrentBatch();
            if (b == null) { MessageBox.Show(this, "先选中一个批次。", "提示"); return; }
            bool toBin = _settings.DeleteToRecycleBin;
            string msg = string.Format("将删除 {0} 个批次文件（{1}）。\n\n{2}\n\n继续？",
                b.ActiveCount, Scanner.FormatBytes(b.Bytes),
                toBin ? "系统支持时会优先送入回收站，否则直接删除。" : "将直接永久删除，不可撤销。");
            if (MessageBox.Show(this, msg, "删除隔离批次",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            string err;
            if (!Quarantine.Purge(b, toBin, out err))
                MessageBox.Show(this, "删除失败: " + err, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Reload();
        }

        private void OpenBatchDir()
        {
            QuarantineBatch b = CurrentBatch();
            string dir = b == null ? AppPaths.QuarantineRoot : b.Dir;
            if (!NativeMethods.OpenFolder(dir))
                MessageBox.Show(this, "系统 Shell 拒绝打开该目录：\n" + dir, "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // =========================================================
    // 设置
    // =========================================================
    public sealed class SettingsForm : Form
    {
        private CheckBox _cbDownloads;
        private CheckBox _cbRecycle;
        private CheckBox _cbTrayAuto;
        private CheckBox _cbConfirm;
        private CheckBox _cbExplorerSelect;
        private ListBox _lbLibs;
        private Button _btnAdd;
        private Button _btnRemove;
        private Button _btnOk;
        private Button _btnCancel;
        private AppSettings _src;

        public AppSettings Result;

        public SettingsForm(AppSettings s)
        {
            _src = s;
            Build();
            Bind();
        }

        private void Build()
        {
            Text = "设置";
            Font = new Font("Microsoft YaHei UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 428);

            _cbDownloads = new CheckBox();
            _cbDownloads.Text = "把 steamapps\\workshop\\downloads 中的未完成下载残片也列入扫描";
            _cbDownloads.SetBounds(18, 18, 520, 24);

            _cbRecycle = new CheckBox();
            _cbRecycle.Text = "彻底删除时送入回收站（可二次恢复）";
            _cbRecycle.SetBounds(18, 46, 520, 24);

            _cbTrayAuto = new CheckBox();
            _cbTrayAuto.Text = "托盘模式下，检测到订阅索引变化时自动重扫并提醒";
            _cbTrayAuto.SetBounds(18, 74, 520, 24);

            _cbConfirm = new CheckBox();
            _cbConfirm.Text = "移入隔离区前弹出确认";
            _cbConfirm.SetBounds(18, 102, 520, 24);

            _cbExplorerSelect = new CheckBox();
            _cbExplorerSelect.Text = "定位文件时允许启动 explorer.exe /select（能在文件夹中选中目标；部分机器会弹 0xc0000142）";
            _cbExplorerSelect.SetBounds(18, 128, 530, 24);

            Label l1 = new Label();
            l1.Text = "额外 Steam 库路径（用于自动探测遗漏的库）：";
            l1.SetBounds(18, 168, 520, 20);

            _lbLibs = new ListBox();
            _lbLibs.SetBounds(18, 192, 430, 96);
            _lbLibs.IntegralHeight = false;

            _btnAdd = new Button();
            _btnAdd.Text = "添加…";
            _btnAdd.SetBounds(456, 192, 86, 28);
            _btnAdd.FlatStyle = FlatStyle.System;
            _btnAdd.Click += delegate { AddLib(); };

            _btnRemove = new Button();
            _btnRemove.Text = "移除";
            _btnRemove.SetBounds(456, 226, 86, 28);
            _btnRemove.FlatStyle = FlatStyle.System;
            _btnRemove.Click += delegate
            {
                if (_lbLibs.SelectedIndex >= 0) _lbLibs.Items.RemoveAt(_lbLibs.SelectedIndex);
            };

            Label l2 = new Label();
            l2.ForeColor = Color.FromArgb(95, 99, 104);
            l2.Text = "数据目录：" + AppPaths.DataDir;
            l2.SetBounds(18, 298, 520, 40);

            _btnOk = new Button();
            _btnOk.Text = "确定";
            _btnOk.SetBounds(360, 366, 86, 30);
            _btnOk.FlatStyle = FlatStyle.System;
            _btnOk.Click += delegate { Collect(); DialogResult = DialogResult.OK; Close(); };

            _btnCancel = new Button();
            _btnCancel.Text = "取消";
            _btnCancel.SetBounds(456, 366, 86, 30);
            _btnCancel.FlatStyle = FlatStyle.System;
            _btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_cbDownloads);
            Controls.Add(_cbRecycle);
            Controls.Add(_cbTrayAuto);
            Controls.Add(_cbConfirm);
            Controls.Add(_cbExplorerSelect);
            Controls.Add(l1);
            Controls.Add(_lbLibs);
            Controls.Add(_btnAdd);
            Controls.Add(_btnRemove);
            Controls.Add(l2);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void Bind()
        {
            _cbDownloads.Checked = _src.IncludeDownloads;
            _cbRecycle.Checked = _src.DeleteToRecycleBin;
            _cbTrayAuto.Checked = _src.TrayAutoScan;
            _cbConfirm.Checked = _src.ConfirmBeforeQuarantine;
            _cbExplorerSelect.Checked = _src.UseExplorerSelect;
            _lbLibs.Items.Clear();
            for (int i = 0; i < _src.ExtraLibraries.Count; i++) _lbLibs.Items.Add(_src.ExtraLibraries[i]);
        }

        private void AddLib()
        {
            using (FolderBrowserDialog f = new FolderBrowserDialog())
            {
                f.Description = "选择 Steam 库根目录（包含 steamapps 的那一层）";
                if (f.ShowDialog(this) != DialogResult.OK) return;
                string p = f.SelectedPath;
                if (!_lbLibs.Items.Contains(p)) _lbLibs.Items.Add(p);
            }
        }

        private void Collect()
        {
            AppSettings r = new AppSettings();
            r.IncludeDownloads = _cbDownloads.Checked;
            r.DeleteToRecycleBin = _cbRecycle.Checked;
            r.TrayAutoScan = _cbTrayAuto.Checked;
            r.ConfirmBeforeQuarantine = _cbConfirm.Checked;
            r.UseExplorerSelect = _cbExplorerSelect.Checked;
            r.WeInstallDir = _src.WeInstallDir;
            for (int i = 0; i < _lbLibs.Items.Count; i++)
            {
                string v = _lbLibs.Items[i] as string;
                if (!string.IsNullOrEmpty(v)) r.ExtraLibraries.Add(v);
            }
            Result = r;
        }
    }

    // =========================================================
    // 托盘常驻：监视 appworkshop_431960.acf 变化
    // =========================================================
    public static class TrayHost
    {
        private static NotifyIcon _icon;
        private static MainForm _owner;
        private static List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private static System.Windows.Forms.Timer _debounce;
        private static bool _scanning;
        private static HashSet<string> _lastOrphans = new HashSet<string>(StringComparer.Ordinal);

        public static void Show(MainForm owner)
        {
            _owner = owner;
            if (_icon == null) CreateIcon();
            _icon.Visible = true;
            InstallWatchers();
            if (_owner != null) _owner.Log("托盘监视已启动。");
        }

        public static void HideTray()
        {
            if (_icon != null) _icon.Visible = false;
            RemoveWatchers();
        }

        private static void CreateIcon()
        {
            _icon = new NotifyIcon();
            try { _icon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _icon.Icon = SystemIcons.Application; }
            _icon.Text = "Wallpaper Engine 壁纸残留清理器";

            ContextMenuStrip m = new ContextMenuStrip();
            m.Font = new Font("Microsoft YaHei UI", 9f);
            m.Items.Add("显示主窗口", null, delegate { ShowMain(); });
            m.Items.Add("立即扫描", null, delegate { ScheduleScan(0); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("打开隔离区", null, delegate
            {
                using (QuarantineForm f = new QuarantineForm()) f.ShowDialog();
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("退出", null, delegate { ExitApp(); });
            _icon.ContextMenuStrip = m;
            _icon.DoubleClick += delegate { ShowMain(); };

            _debounce = new System.Windows.Forms.Timer();
            _debounce.Interval = 1200;
            _debounce.Tick += delegate
            {
                _debounce.Stop();
                DoScanAsync();
            };
        }

        private static void ShowMain()
        {
            if (_owner == null) return;
            _owner.Show();
            _owner.WindowState = FormWindowState.Normal;
            _owner.Activate();
        }

        private static void ExitApp()
        {
            RemoveWatchers();
            if (_icon != null) { _icon.Visible = false; _icon.Dispose(); _icon = null; }
            Application.Exit();
        }

        private static void InstallWatchers()
        {
            RemoveWatchers();
            List<SteamLibrary> libs = SteamLocator.Libraries();
            for (int i = 0; i < libs.Count; i++)
            {
                string wsDir = Path.GetDirectoryName(libs[i].AcfPath);
                if (wsDir == null || !Directory.Exists(wsDir)) continue;
                try
                {
                    FileSystemWatcher w = new FileSystemWatcher(wsDir);
                    w.Filter = "appworkshop_" + SteamLocator.WeAppId + ".acf";
                    w.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime;
                    w.Changed += delegate { ScheduleScan(1500); };
                    w.Created += delegate { ScheduleScan(1500); };
                    w.Renamed += delegate { ScheduleScan(1500); };
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch { }
            }
        }

        private static void RemoveWatchers()
        {
            for (int i = 0; i < _watchers.Count; i++)
            {
                try
                {
                    _watchers[i].EnableRaisingEvents = false;
                    _watchers[i].Dispose();
                }
                catch { }
            }
            _watchers.Clear();
        }

        private static void ScheduleScan(int delayMs)
        {
            if (_debounce == null) return;
            _debounce.Stop();
            _debounce.Interval = Math.Max(300, delayMs);
            _debounce.Start();
        }

        private static void DoScanAsync()
        {
            if (_scanning) return;
            _scanning = true;

            ThreadPool.QueueUserWorkItem(delegate
            {
                ScanReport rep = null;
                try
                {
                    AppSettings s = AppSettings.Load();
                    if (!s.TrayAutoScan) { _scanning = false; return; }
                    ScanOptions opt = new ScanOptions();
                    opt.IncludeDownloads = s.IncludeDownloads;
                    opt.WeInstallDir = s.WeInstallDir;
                    opt.ExtraLibraries.AddRange(s.ExtraLibraries);
                    rep = Scanner.Scan(opt);
                }
                catch { }

                _scanning = false;
                if (rep == null) return;

                HashSet<string> now = new HashSet<string>(StringComparer.Ordinal);
                long bytes = 0;
                for (int i = 0; i < rep.Items.Count; i++)
                {
                    if (!rep.Items[i].Deletable) continue;
                    now.Add(rep.Items[i].Id);
                    bytes += rep.Items[i].Bytes;
                }

                int added = 0;
                foreach (string id in now)
                {
                    if (!_lastOrphans.Contains(id)) added++;
                }
                _lastOrphans = now;

                if (added > 0 && _icon != null)
                {
                    ShowBalloon("发现新的壁纸残片",
                        string.Format("有 {0} 个已退订壁纸仍占用磁盘，共 {1}。双击托盘图标处理。",
                            now.Count, Scanner.FormatBytes(bytes)));
                }
            });
        }

        private static void ShowBalloon(string title, string text)
        {
            try
            {
                if (_icon == null) return;
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = text;
                _icon.BalloonTipIcon = ToolTipIcon.Info;
                _icon.ShowBalloonTip(6000);
            }
            catch { }
        }
    }
}
