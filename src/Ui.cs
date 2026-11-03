// WinForms 主界面
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WeCleaner
{
    public sealed class MainForm : Form
    {
        private ToolStrip _tool;
        private ToolStripButton _btnScan;
        private ToolStripButton _btnCheckAll;
        private ToolStripButton _btnUncheckAll;
        private ToolStripButton _btnQuarantine;
        private ToolStripButton _btnQuarantineMgr;
        private ToolStripButton _btnOpenWe;
        private ToolStripButton _btnSettings;
        private ToolStripButton _btnTray;

        private SplitContainer _mainSplit;
        private DataGridView _grid;
        private PictureBox _preview;
        private Label _titleLabel;
        private Label _subLabel;
        private TextBox _detail;
        private RichTextBox _log;
        private SplitContainer _rightSplit;
        private StatusStrip _status;
        private ToolStripStatusLabel _statLeft;
        private ToolStripStatusLabel _statMid;
        private ToolStripStatusLabel _statRight;
        private ToolStripProgressBar _progress;

        private AppSettings _settings;
        private ScanReport _report;
        private BackgroundWorker _worker;
        private bool _scanning;

        private sealed class RowTag
        {
            public WallpaperItem Item;
        }

        public MainForm()
        {
            _settings = AppSettings.Load();
            BuildUi();
            Load += delegate { BeginScan(); };
        }

        // ---------------- 构建界面 ----------------

        private void BuildUi()
        {
            Text = "Wallpaper Engine 壁纸残留清理器";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 560);
            Size = new Size(1240, 800);
            Font = new Font("Microsoft YaHei UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { try { Icon = SystemIcons.Application; } catch { } }

            // ---- 工具栏 ----
            _tool = new ToolStrip();
            _tool.GripStyle = ToolStripGripStyle.Hidden;
            _tool.ImageScalingSize = new Size(16, 16);
            _tool.Padding = new Padding(6, 3, 6, 3);

            _btnScan = MakeButton("重新扫描 (F5)", delegate { BeginScan(); });
            _btnCheckAll = MakeButton("勾选全部可清理", delegate { CheckAll(true); });
            _btnUncheckAll = MakeButton("全部取消", delegate { CheckAll(false); });
            _btnQuarantine = MakeButton("移入隔离区", delegate { QuarantineChecked(); });
            _btnQuarantineMgr = MakeButton("隔离区管理", delegate { OpenQuarantineManager(); });
            _btnOpenWe = MakeButton("打开 WE 配置目录", delegate { OpenWeFolder(); });
            _btnSettings = MakeButton("设置", delegate { OpenSettings(); });
            _btnTray = MakeButton("后台监视", delegate { MinimizeToTray(); });

            _tool.Items.Add(_btnScan);
            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(_btnCheckAll);
            _tool.Items.Add(_btnUncheckAll);
            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(_btnQuarantine);
            _tool.Items.Add(_btnQuarantineMgr);
            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(_btnOpenWe);
            _tool.Items.Add(_btnSettings);
            _tool.Items.Add(_btnTray);
            Controls.Add(_tool);

            // ---- 表格 ----
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _grid.BackgroundColor = Color.White;
            _grid.BorderStyle = BorderStyle.None;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.ColumnHeadersHeight = 30;
            _grid.RowTemplate.Height = 28;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 250);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 64, 67);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 0, 0);
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 240, 254);
            _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(32, 33, 36);
            _grid.DefaultCellStyle.Padding = new Padding(4, 0, 0, 0);
            _grid.GridColor = Color.FromArgb(232, 234, 237);

            AddColumn("sel", "", 34, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("level", "状态", 76, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("title", "壁纸标题", 250, DataGridViewContentAlignment.MiddleLeft, true, 90);
            AddColumn("size", "占用", 78, DataGridViewContentAlignment.MiddleRight, false);
            AddColumn("id", "Workshop ID", 112, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("reason", "判定依据", 300, DataGridViewContentAlignment.MiddleLeft, true, 110);

            _grid.Columns["sel"].ReadOnly = false;
            _grid.Columns["title"].ReadOnly = true;
            _grid.Columns["size"].ReadOnly = true;
            _grid.Columns["level"].ReadOnly = true;
            _grid.Columns["id"].ReadOnly = true;
            _grid.Columns["reason"].ReadOnly = true;

            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty)
                {
                    try { _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); }
                    catch { }
                }
            };
            _grid.SelectionChanged += delegate { ShowSelectedDetail(); };
            _grid.CellMouseDown += GridCellMouseDown;
            _grid.KeyDown += GridKeyDown;

            ContextMenuStrip menu = BuildContextMenu();
            _grid.ContextMenuStrip = menu;

            // ---- 右侧面板 ----
            _preview = new PictureBox();
            _preview.Dock = DockStyle.Top;
            _preview.Height = 200;
            _preview.SizeMode = PictureBoxSizeMode.Zoom;
            _preview.BackColor = Color.FromArgb(245, 246, 248);
            _preview.BorderStyle = BorderStyle.FixedSingle;

            _detail = new TextBox();
            _detail.Dock = DockStyle.Fill;
            _detail.ReadOnly = true;
            _detail.BorderStyle = BorderStyle.None;
            _detail.BackColor = Color.FromArgb(252, 252, 253);
            _detail.Font = new Font("Microsoft YaHei UI", 8.5f);
            _detail.WordWrap = true;
            _detail.Multiline = true;
            _detail.ScrollBars = ScrollBars.Vertical;
            _detail.Padding = new Padding(6, 4, 2, 4);

            _titleLabel = new Label();
            _titleLabel.Dock = DockStyle.None;
            _titleLabel.AutoSize = false;
            _titleLabel.SetBounds(6, 6, 260, 26);
            _titleLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _titleLabel.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            _titleLabel.ForeColor = Color.FromArgb(32, 33, 36);
            _titleLabel.BackColor = Color.FromArgb(252, 252, 253);
            _titleLabel.AutoEllipsis = true;
            _titleLabel.Text = "";

            _subLabel = new Label();
            _subLabel.Dock = DockStyle.None;
            _subLabel.AutoSize = false;
            _subLabel.SetBounds(6, 33, 260, 20);
            _subLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _subLabel.TextAlign = ContentAlignment.TopLeft;
            _subLabel.Font = new Font("Microsoft YaHei UI", 8f);
            _subLabel.ForeColor = Color.FromArgb(95, 99, 104);
            _subLabel.BackColor = Color.FromArgb(252, 252, 253);
            _subLabel.AutoEllipsis = true;
            _subLabel.Text = "";

            Panel headPanel = new Panel();
            headPanel.Dock = DockStyle.Top;
            headPanel.Height = 58;
            headPanel.BackColor = Color.FromArgb(252, 252, 253);
            headPanel.Controls.Add(_titleLabel);
            headPanel.Controls.Add(_subLabel);

            Panel detailHost = new Panel();
            detailHost.Dock = DockStyle.Fill;
            detailHost.BackColor = Color.FromArgb(252, 252, 253);
            detailHost.Controls.Add(_detail);
            detailHost.Controls.Add(headPanel);

            _rightSplit = new SplitContainer();
            _rightSplit.Dock = DockStyle.Fill;
            _rightSplit.Orientation = Orientation.Vertical;
            _rightSplit.Size = new Size(420, 640);
            _rightSplit.Panel1.Controls.Add(detailHost);
            _rightSplit.Panel2.Controls.Add(_preview);
            _rightSplit.Panel1MinSize = 120;
            _rightSplit.Panel2MinSize = 140;

            // ---- 日志 ----
            _log = new RichTextBox();
            _log.Dock = DockStyle.Fill;
            _log.ReadOnly = true;
            _log.BorderStyle = BorderStyle.FixedSingle;
            _log.BackColor = Color.FromArgb(250, 250, 250);
            _log.Font = new Font("Consolas", 8.5f);
            _log.ForeColor = Color.FromArgb(95, 99, 104);

            _mainSplit = new SplitContainer();
            _mainSplit.Dock = DockStyle.Fill;
            _mainSplit.Orientation = Orientation.Vertical;
            _mainSplit.Size = new Size(1160, 640);
            _mainSplit.Panel1.Controls.Add(_grid);
            _mainSplit.Panel2.Controls.Add(_rightSplit);
            _mainSplit.Panel1MinSize = 420;
            _mainSplit.Panel2MinSize = 300;

            SplitContainer vertical = new SplitContainer();
            vertical.Dock = DockStyle.Fill;
            vertical.Orientation = Orientation.Horizontal;
            vertical.Size = new Size(1160, 720);
            vertical.Panel1.Controls.Add(_mainSplit);
            vertical.Panel2.Controls.Add(_log);
            vertical.Panel2MinSize = 60;
            vertical.FixedPanel = FixedPanel.Panel2;
            vertical.IsSplitterFixed = false;

            Controls.Add(vertical);
            Controls.SetChildIndex(vertical, 0);

            // ---- 状态栏 ----
            _status = new StatusStrip();
            _status.SizingGrip = true;
            _statLeft = new ToolStripStatusLabel("就绪");
            _statLeft.Spring = true;
            _statLeft.TextAlign = ContentAlignment.MiddleLeft;
            _statMid = new ToolStripStatusLabel("");
            _statMid.BorderSides = ToolStripStatusLabelBorderSides.Left;
            _statMid.BorderStyle = Border3DStyle.Etched;
            _statRight = new ToolStripStatusLabel("");
            _statRight.BorderSides = ToolStripStatusLabelBorderSides.Left;
            _statRight.BorderStyle = Border3DStyle.Etched;
            _progress = new ToolStripProgressBar();
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = false;
            _progress.Width = 120;

            _status.Items.Add(_statLeft);
            _status.Items.Add(_progress);
            _status.Items.Add(_statMid);
            _status.Items.Add(_statRight);
            Controls.Add(_status);

            // Dock 布局按 z-order 从后往前处理：先 dock 边缘控件，Fill 必须最后（index 0）
            Controls.SetChildIndex(vertical, 0);
            Controls.SetChildIndex(_status, 1);
            Controls.SetChildIndex(_tool, 2);

            Load += delegate
            {
                try
                {
                    int w = ClientSize.Width;
                    int want = Math.Max(_mainSplit.Panel1MinSize, w - 420);
                    int max = w - _mainSplit.Panel2MinSize;
                    if (want > max) want = max;
                    _mainSplit.SplitterDistance = want;

                    int h = vertical.Height;
                    int wantH = Math.Max(200, h - 116);
                    int maxH = h - vertical.Panel2MinSize;
                    if (wantH > maxH) wantH = maxH;
                    vertical.SplitterDistance = wantH;

                    _rightSplit.SplitterDistance = Math.Max(160, _rightSplit.Width - 200);

                    NativeMethods.SetWindowTheme(_grid.Handle, "Explorer", null);
                }
                catch (Exception ex) { Log("界面初始化提示: " + ex.Message); }
            };
        }

        private ToolStripButton MakeButton(string text, EventHandler onClick)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.Padding = new Padding(4, 2, 4, 2);
            b.Click += onClick;
            return b;
        }

        private void AddColumn(string name, string header, int width, DataGridViewContentAlignment align, bool fill, int fillWeight)
        {
            if (name == "sel")
            {
                DataGridViewCheckBoxColumn chk = new DataGridViewCheckBoxColumn();
                chk.Name = "sel";
                chk.HeaderText = "";
                chk.Width = width;
                chk.SortMode = DataGridViewColumnSortMode.NotSortable;
                chk.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                chk.FalseValue = false;
                chk.TrueValue = true;
                chk.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                _grid.Columns.Add(chk);
                return;
            }

            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = header;
            c.Width = width;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.DefaultCellStyle.Alignment = align;
            if (fill)
            {
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                c.FillWeight = fillWeight;
            }
            else
            {
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                c.Resizable = DataGridViewTriState.True;
            }
            _grid.Columns.Add(c);
        }

        private void AddColumn(string name, string header, int width, DataGridViewContentAlignment align, bool fill)
        {
            AddColumn(name, header, width, align, fill, 0);
        }

        private ContextMenuStrip BuildContextMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Font = Font;
            m.Items.Add("移入隔离区", null, delegate { QuarantineChecked(); });
            m.Items.Add("加入保留白名单", null, delegate { AddToKeepList(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("打开所在文件夹", null, delegate { OpenSelectedFolder(); });
            m.Items.Add("打开创意工坊页面", null, delegate { OpenWorkshopPage(); });
            m.Items.Add("复制 Workshop ID", null, delegate { CopySelectedId(); });
            m.Items.Add("复制完整路径", null, delegate { CopySelectedPath(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("重新扫描", null, delegate { BeginScan(); });
            return m;
        }

        // ---------------- 扫描 ----------------

        private void BeginScan()
        {
            if (_scanning) return;
            _scanning = true;
            _grid.Rows.Clear();
            _detail.Clear();
            _preview.Image = null;
            _progress.Visible = true;
            _statLeft.Text = "正在扫描 Steam 库与 Workshop 索引…";
            UseWaitCursor = true;
            SetButtonsEnabled(false);

            _worker = new BackgroundWorker();
            _worker.DoWork += delegate (object s, DoWorkEventArgs e)
            {
                ScanOptions opt = new ScanOptions();
                opt.IncludeDownloads = _settings.IncludeDownloads;
                opt.ReadTitles = true;
                opt.WeInstallDir = _settings.WeInstallDir;
                opt.ExtraLibraries.AddRange(_settings.ExtraLibraries);
                e.Result = Scanner.Scan(opt);
            };
            _worker.RunWorkerCompleted += delegate (object s, RunWorkerCompletedEventArgs e)
            {
                _scanning = false;
                _progress.Visible = false;
                UseWaitCursor = false;
                SetButtonsEnabled(true);

                if (e.Error != null)
                {
                    Log("扫描失败: " + e.Error.Message);
                    _statLeft.Text = "扫描失败";
                    return;
                }
                _report = e.Result as ScanReport;
                PopulateGrid();
            };
            _worker.RunWorkerAsync();
        }

        private void SetButtonsEnabled(bool on)
        {
            _btnScan.Enabled = on;
            _btnCheckAll.Enabled = on;
            _btnUncheckAll.Enabled = on;
            _btnQuarantine.Enabled = on;
        }

        private void PopulateGrid()
        {
            _grid.SuspendLayout();
            _grid.Rows.Clear();

            if (_report == null) { _grid.ResumeLayout(); return; }

            int shown = 0;
            for (int i = 0; i < _report.Items.Count; i++)
            {
                WallpaperItem it = _report.Items[i];
                if (it.Level == OrphanLevel.MissingOnDisk) continue;

                int idx = _grid.Rows.Add();
                DataGridViewRow row = _grid.Rows[idx];
                row.Cells["sel"].Value = it.Deletable;
                row.Cells["level"].Value = it.LevelText;
                row.Cells["title"].Value = string.IsNullOrEmpty(it.Title) ? "(无法读取标题)" : it.Title;
                row.Cells["size"].Value = Scanner.FormatBytes(it.Bytes);
                row.Cells["id"].Value = it.Id;
                row.Cells["reason"].Value = it.Reason;
                row.Tag = new RowTag();
                ((RowTag)row.Tag).Item = it;

                ApplyRowStyle(row, it);
                shown++;
            }

            _grid.ResumeLayout();

            if (_grid.Rows.Count > 0)
            {
                _grid.ClearSelection();
                _grid.CurrentCell = _grid.Rows[0].Cells["title"];
                _grid.Rows[0].Selected = true;
                ShowSelectedDetail();
            }

            if (_report.Libraries.Count == 0)
            {
                Log("未发现 Steam 库。可在「设置」里手动添加库路径。");
            }
            for (int i = 0; i < _report.Warnings.Count; i++) Log("警告: " + _report.Warnings[i]);

            Log(string.Format("扫描完成：{0} 个库 / 共 {1} 项 / 可清理 {2} 项 / 预计释放 {3}",
                _report.Libraries.Count, shown, _report.DeletableCount, Scanner.FormatBytes(_report.DeletableBytes)));
            for (int i = 0; i < _report.WeConfigPaths.Count; i++)
                Log("保护来源: " + _report.WeConfigPaths[i]);

            UpdateStatus();
            _statLeft.Text = string.Format("扫描完成 · {0} 个库 · {1} 项壁纸", _report.Libraries.Count, shown);
        }

        private void ApplyRowStyle(DataGridViewRow row, WallpaperItem it)
        {
            Color bg = Color.White;
            Color fg = Color.FromArgb(32, 33, 36);
            Color levelFg = Color.FromArgb(95, 99, 104);

            switch (it.Level)
            {
                case OrphanLevel.Protected:
                    bg = Color.FromArgb(240, 244, 240); levelFg = Color.FromArgb(46, 125, 50); break;
                case OrphanLevel.Subscribed:
                    bg = Color.White; levelFg = Color.FromArgb(46, 125, 50); break;
                case OrphanLevel.FavoriteHint:
                    bg = Color.FromArgb(240, 246, 255); levelFg = Color.FromArgb(25, 103, 210); break;
                case OrphanLevel.BackupHint:
                    bg = Color.FromArgb(246, 246, 250); levelFg = Color.FromArgb(95, 99, 104); break;
                case OrphanLevel.Medium:
                    bg = Color.FromArgb(255, 250, 235); levelFg = Color.FromArgb(176, 110, 0); break;
                case OrphanLevel.Strong:
                    bg = Color.FromArgb(255, 243, 236); levelFg = Color.FromArgb(200, 80, 0); break;
                case OrphanLevel.Critical:
                    bg = Color.FromArgb(253, 237, 237); levelFg = Color.FromArgb(197, 34, 31); break;
                case OrphanLevel.DownloadFragment:
                    bg = Color.FromArgb(247, 242, 255); levelFg = Color.FromArgb(123, 31, 162); break;
                default:
                    bg = Color.FromArgb(245, 245, 245); levelFg = Color.Gray; break;
            }

            row.DefaultCellStyle.BackColor = bg;
            row.DefaultCellStyle.ForeColor = fg;
            row.Cells["level"].Style.ForeColor = levelFg;
            row.Cells["level"].Style.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            row.Cells["size"].Style.ForeColor = it.Deletable ? Color.FromArgb(197, 34, 31) : Color.FromArgb(95, 99, 104);
            row.Cells["reason"].Style.ForeColor = Color.FromArgb(95, 99, 104);
            row.Cells["id"].Style.Font = new Font("Consolas", 9f);
        }

        private void UpdateStatus()
        {
            if (_report == null) return;
            _statMid.Text = string.Format("可清理 {0} 项 / {1}",
                _report.DeletableCount, Scanner.FormatBytes(_report.DeletableBytes));

            long qbytes = 0;
            List<QuarantineBatch> batches = Quarantine.ListBatches();
            for (int i = 0; i < batches.Count; i++) qbytes += batches[i].Bytes;
            _statRight.Text = string.Format("隔离区 {0}", Scanner.FormatBytes(qbytes));

            int checkedCount = 0;
            long checkedBytes = 0;
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                if (!IsChecked(_grid.Rows[i])) continue;
                RowTag t = _grid.Rows[i].Tag as RowTag;
                if (t == null) continue;
                checkedCount++;
                checkedBytes += t.Item.Bytes;
            }
            _statLeft.Text = string.Format("已勾选 {0} 项 · 将释放 {1}", checkedCount, Scanner.FormatBytes(checkedBytes));
        }

        // ---------------- 勾选 ----------------

        private bool IsChecked(DataGridViewRow row)
        {
            object v = row.Cells["sel"].Value;
            if (v == null) return false;
            if (v is bool) return (bool)v;
            bool b;
            if (bool.TryParse(v.ToString(), out b)) return b;
            return false;
        }

        private void SetChecked(DataGridViewRow row, bool on)
        {
            row.Cells["sel"].Value = on;
        }

        private void CheckAll(bool on)
        {
            _grid.EndEdit();
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                DataGridViewRow row = _grid.Rows[i];
                RowTag t = row.Tag as RowTag;
                if (t == null) continue;
                if (on && !t.Item.Deletable) continue;
                SetChecked(row, on);
            }
            UpdateStatus();
        }

        private List<WallpaperItem> GetCheckedItems()
        {
            _grid.EndEdit();
            List<WallpaperItem> list = new List<WallpaperItem>();
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                DataGridViewRow row = _grid.Rows[i];
                if (!IsChecked(row)) continue;
                RowTag t = row.Tag as RowTag;
                if (t == null) continue;
                list.Add(t.Item);
            }
            return list;
        }

        private WallpaperItem GetCurrentItem()
        {
            if (_grid.CurrentRow == null) return null;
            RowTag t = _grid.CurrentRow.Tag as RowTag;
            return t == null ? null : t.Item;
        }

        // ---------------- 详情 ----------------

        private void ShowSelectedDetail()
        {
            WallpaperItem it = GetCurrentItem();
            _detail.Clear();
            if (it == null)
            {
                _titleLabel.Text = "";
                _subLabel.Text = "";
                _preview.Image = null;
                return;
            }

            _titleLabel.Text = string.IsNullOrEmpty(it.Title) ? ("Workshop " + it.Id) : it.Title;
            _subLabel.Text = it.Id + (string.IsNullOrEmpty(it.Type) ? "" : "  ·  " + it.Type)
                + (it.InWeFavorites ? "  ·  收藏夹" : "");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("状态      " + it.LevelText);
            sb.AppendLine("判定      " + it.Reason);
            sb.AppendLine("占用      " + Scanner.FormatBytes(it.Bytes) + "  (" + it.FileCount + " 个文件)");
            sb.AppendLine();
            sb.AppendLine("── ACF 信号 ──");
            sb.AppendLine("Installed   " + (it.InInstalled ? "有" : "无"));
            sb.AppendLine("Details     " + (it.InDetails ? "有" : "无"));
            sb.AppendLine("subscribedby " + (it.HasSubscribedBy ? it.SubscribedBy : "无"));
            if (it.AcfSize > 0) sb.AppendLine("ACF 记录大小 " + Scanner.FormatBytes(it.AcfSize));
            if (it.TimeUpdated > 0)
                sb.AppendLine("更新时间    " + FromUnix(it.TimeUpdated).ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();
            sb.AppendLine("── 路径 ──");
            sb.AppendLine(it.ContentPath);
            sb.AppendLine();
            sb.AppendLine("── 所在库 ──");
            sb.AppendLine(it.LibraryRoot);

            _detail.Text = sb.ToString();
            try
            {
                _detail.SelectionStart = 0;
                _detail.SelectionLength = 0;
            }
            catch { }

            // 预览图
            try
            {
                if (_preview.Image != null)
                {
                    Image old = _preview.Image;
                    _preview.Image = null;
                    old.Dispose();
                }
            }
            catch { }
            // 预览图：先解码到独立 Bitmap，彻底脱离文件流。
            // 直接把 Image.FromStream 的结果挂到 PictureBox 上，GIF 动画帧回读时会拿到已关闭的流，
            // 触发「GDI+ 中发生一般性错误」(SelectActiveFrame)。
            if (!string.IsNullOrEmpty(it.PreviewPath) && File.Exists(it.PreviewPath))
            {
                try
                {
                    FileInfo pf = new FileInfo(it.PreviewPath);
                    if (pf.Length > 0 && pf.Length <= 32L * 1024 * 1024)
                    {
                        byte[] data = File.ReadAllBytes(it.PreviewPath);
                        using (MemoryStream ms = new MemoryStream(data))
                        using (Image src = Image.FromStream(ms, false, false))
                        {
                            _preview.Image = new Bitmap(src);
                        }
                    }
                }
                catch
                {
                    try { _preview.Image = null; }
                    catch { }
                }
            }
        }

        private static DateTime FromUnix(long sec)
        {
            try
            {
                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return epoch.AddSeconds(sec).ToLocalTime();
            }
            catch { return DateTime.MinValue; }
        }

        // ---------------- 操作 ----------------

        private void QuarantineChecked()
        {
            List<WallpaperItem> items = GetCheckedItems();
            if (items.Count == 0)
            {
                Info("没有勾选任何项目。\n先用「勾选全部可清理」把已退订的残片选上。");
                return;
            }

            long bytes = 0;
            for (int i = 0; i < items.Count; i++) bytes += items[i].Bytes;

            if (_settings.ConfirmBeforeQuarantine)
            {
                string msg = string.Format(
                    "将把 {0} 项壁纸（{1}）从 Workshop 目录移入隔离区。\n\n" +
                    "· 文件不会被立刻删除，随时可以一键恢复\n" +
                    "· 隔离区位置：{2}\n\n继续？",
                    items.Count, Scanner.FormatBytes(bytes), AppPaths.QuarantineRoot);
                if (MessageBox.Show(this, msg, "移入隔离区", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
            }

            if (Quarantine.IsWeRunning())
            {
                DialogResult r = MessageBox.Show(this,
                    "检测到 Wallpaper Engine 正在运行，部分文件可能被占用。\n建议先退出托盘里的 WE 再清理。仍要继续吗？",
                    "WE 正在运行", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (r != DialogResult.OK) return;
            }

            UseWaitCursor = true;
            List<string> errors;
            QuarantineBatch batch = Quarantine.MoveToQuarantine(items, out errors);
            UseWaitCursor = false;

            for (int i = 0; i < errors.Count; i++) Log("失败: " + errors[i]);
            Log(string.Format("已隔离 {0} 项 / {1} → {2}",
                batch.Entries.Count, Scanner.FormatBytes(batch.Bytes), batch.Dir));

            BeginScan();
        }

        private void AddToKeepList()
        {
            WallpaperItem it = GetCurrentItem();
            if (it == null) return;
            HashSet<string> keep = UserKeepList.Load();
            keep.Add(it.Id);
            UserKeepList.Save(keep);
            Log("已加入保留白名单: " + it.Id);
            BeginScan();
        }

        private void OpenSelectedFolder()
        {
            WallpaperItem it = GetCurrentItem();
            if (it == null) return;
            NativeMethods.AllowNewExplorerProcess = _settings.UseExplorerSelect;
            if (NativeMethods.RevealPath(it.ContentPath))
            {
                Log("已打开: " + it.ContentPath);
                return;
            }
            Log("无法打开目录: " + it.ContentPath);
            Log("  诊断: " + NativeMethods.LastDiag);
            try { Clipboard.SetText(it.ContentPath); Log("  路径已复制到剪贴板，可直接粘到资源管理器地址栏。"); }
            catch { }
        }

        private void OpenWorkshopPage()
        {
            WallpaperItem it = GetCurrentItem();
            if (it == null) return;
            string url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + it.Id;
            if (!NativeMethods.OpenUrl(url)) Log("无法打开页面: " + url + "  " + NativeMethods.LastDiag);
            else Log("已在浏览器中打开创意工坊页面: " + it.Id);
        }

        private void CopySelectedPath()
        {
            WallpaperItem it = GetCurrentItem();
            if (it == null) return;
            try { Clipboard.SetText(it.ContentPath); Log("已复制路径: " + it.ContentPath); }
            catch { }
        }

        private void CopySelectedId()
        {
            WallpaperItem it = GetCurrentItem();
            if (it == null) return;
            try { Clipboard.SetText(it.Id); }
            catch { }
            Log("已复制 ID: " + it.Id);
        }

        private void OpenQuarantineManager()
        {
            using (QuarantineForm f = new QuarantineForm())
            {
                f.ShowDialog(this);
            }
            UpdateStatus();
        }

        private void OpenWeFolder()
        {
            if (_report != null && _report.WeConfigPaths.Count > 0)
            {
                if (NativeMethods.RevealPath(_report.WeConfigPaths[0])) return;
            }
            List<SteamLibrary> libs = SteamLocator.Libraries();
            string dir = SteamLocator.FindWeInstallDir(libs);
            if (dir != null && Directory.Exists(dir))
            {
                if (NativeMethods.OpenFolder(dir)) return;
            }
            Info("没有找到 Wallpaper Engine 安装目录，或系统 Shell 拒绝打开。");
        }

        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(_settings))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    _settings = f.Result;
                    _settings.Save();
                    Log("设置已保存。库路径变更将在下次扫描生效。");
                    BeginScan();
                }
            }
        }

        private void MinimizeToTray()
        {
            TrayHost.Show(this);
            Hide();
            Log("已最小化到系统托盘，正在监视 Workshop 索引变化。");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            TrayHost.HideTray();
            base.OnFormClosing(e);
        }

        // ---------------- 事件 ----------------

        private void GridCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.ColumnIndex == _grid.Columns["sel"].Index && e.RowIndex >= 0)
            {
                // 让 checkbox 正常切换
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells["sel"];
            }
        }

        private void GridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { BeginScan(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Space && _grid.CurrentRow != null)
            {
                SetChecked(_grid.CurrentRow, !IsChecked(_grid.CurrentRow));
                UpdateStatus();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.A)
            {
                _grid.SelectAll();
                e.Handled = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5) { BeginScan(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------- 日志 ----------------

        public void Log(string text)
        {
            if (_log == null) return;
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine;
            if (_log.TextLength > 40000) _log.Clear();
            _log.AppendText(line);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        private void Info(string msg)
        {
            MessageBox.Show(this, msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
