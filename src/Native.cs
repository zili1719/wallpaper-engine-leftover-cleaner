// Win32 互操作：控制台附加、回收站删除、DPI、Shell 打开
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WeCleaner
{
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetConsoleOutputCP(uint wCodePageID);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetConsoleCP(uint wCodePageID);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        internal static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();

        private const int ATTACH_PARENT_PROCESS = -1;

        internal static bool TryAttachParentConsole()
        {
            try { return AttachConsole(ATTACH_PARENT_PROCESS); }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;

        /// <summary>把文件/目录送进回收站。失败返回 false。</summary>
        internal static bool SHFileOperationDelete(string path)
        {
            try
            {
                SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
                op.wFunc = FO_DELETE;
                op.pFrom = path + "\0\0";
                op.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI);
                int r = SHFileOperation(ref op);
                return r == 0 && !op.fAnyOperationsAborted;
            }
            catch { return false; }
        }

        // ---- Shell 打开：不发新进程，避免再触发 explorer.exe 0xc0000142 ----

        [DllImport("ole32.dll")]
        private static extern int OleInitialize(IntPtr pvReserved);

        [DllImport("ole32.dll")]
        private static extern void OleUninitialize();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr ILCreateFromPath(string pszPath);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, uint dwFlags);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        /// <summary>最近一次 Shell 调用的诊断信息，供界面日志展示。</summary>
        internal static string LastDiag = "";

        private static readonly object _shellLock = new object();
        private static DateTime _lastLaunch = DateTime.MinValue;
        private static string _lastTarget = "";

        /// <summary>
        /// 同一目标 900ms 内只拉起一次 explorer。
        /// 连续快速拉起多个 explorer.exe 进程会让第二个在 loader 阶段初始化失败，
        /// 系统随即弹出「explorer.exe - 应用程序错误 0xc0000142」。
        /// </summary>
        private static bool Debounced(string target)
        {
            lock (_shellLock)
            {
                DateTime now = DateTime.UtcNow;
                if (string.Equals(_lastTarget, target, StringComparison.OrdinalIgnoreCase)
                    && (now - _lastLaunch).TotalMilliseconds < 900)
                {
                    return true; // 视为已处理，直接吞掉这次重复请求
                }
                _lastTarget = target;
                _lastLaunch = now;
                return false;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ShellExecute(IntPtr hwnd, string lpOperation, string lpFile, string lpParameters, string lpDirectory, int nShowCmd);

        private const int SW_SHOWNORMAL = 1;

        /// <summary>路径 1：拉起 explorer.exe /select。多数环境最可靠，但会新建进程。</summary>
        private static bool TryExplorerSelect(string path, System.Collections.Generic.List<string> diags)
        {
            if (Debounced("select:" + path))
            {
                diags.Add("select: debounced (already launching)");
                return true;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "explorer.exe";
                psi.Arguments = "/select,\"" + path + "\"";
                psi.UseShellExecute = true;
                psi.ErrorDialog = false;
                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        try { p.Dispose(); }
                        catch { }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                diags.Add("explorer /select: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        /// <summary>路径 2：SHOpenFolderAndSelectItems，不新建进程。DCOM 受限时会返回 E_ACCESSDENIED。</summary>
        private static bool TryShellSelect(string path, System.Collections.Generic.List<string> diags)
        {
            int oleHr = 0;
            bool oleOwned = false;
            try
            {
                oleHr = OleInitialize(IntPtr.Zero);
                oleOwned = (oleHr == 0);
            }
            catch (Exception ex)
            {
                diags.Add("OleInitialize: " + ex.Message);
            }

            IntPtr pidl = IntPtr.Zero;
            try
            {
                pidl = ILCreateFromPath(path);
                if (pidl == IntPtr.Zero)
                {
                    diags.Add("ILCreateFromPath NULL, win32=" + Marshal.GetLastWin32Error());
                    return false;
                }
                int hr = SHOpenFolderAndSelectItems(pidl, 0, null, 0);
                if (hr != 0)
                {
                    diags.Add(string.Format("SHOpenFolderAndSelectItems hr=0x{0:X8} (oleInit=0x{1:X8})", hr, oleHr));
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                diags.Add("SHOpenFolderAndSelectItems: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
            finally
            {
                if (pidl != IntPtr.Zero)
                {
                    try { ILFree(pidl); }
                    catch { }
                }
                if (oleOwned)
                {
                    try { OleUninitialize(); }
                    catch { }
                }
            }
        }

        /// <summary>路径 3：ShellExecute 打开目录本身。</summary>
        private static bool TryShellExecuteOpen(string folder, System.Collections.Generic.List<string> diags)
        {
            if (Debounced("open:" + folder))
            {
                diags.Add("shellexecute: debounced (already launching)");
                return true;
            }
            try
            {
                IntPtr rc = ShellExecute(IntPtr.Zero, "open", folder, null, null, SW_SHOWNORMAL);
                long code = rc.ToInt64();
                if (code > 32) return true;
                diags.Add("ShellExecute.open rc=" + code);
            }
            catch (Exception ex)
            {
                diags.Add("ShellExecute: " + ex.Message);
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = folder;
                psi.UseShellExecute = true;
                psi.ErrorDialog = false;
                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        try { p.Dispose(); }
                        catch { }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                diags.Add("Process.Start(dir): " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        /// <summary>是否允许用 explorer.exe /select 定位。会新建进程，默认关闭。</summary>
        internal static bool AllowNewExplorerProcess = false;

        /// <summary>在资源管理器中定位路径，逐级降级，任一级成功即返回。</summary>
        internal static bool RevealPath(string path)
        {
            LastDiag = "";
            if (string.IsNullOrEmpty(path)) { LastDiag = "empty path"; return false; }

            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) { LastDiag = "GetFullPath: " + ex.Message; return false; }

            bool isFile = File.Exists(full);
            bool isDir = Directory.Exists(full);
            if (!isFile && !isDir) { LastDiag = "not found: " + full; return false; }

            System.Collections.Generic.List<string> diags = new System.Collections.Generic.List<string>();

            // 1) 在文件夹里选中该项。不新建进程，最理想；Shell 通道受限时会返回 E_ACCESSDENIED。
            if (TryShellSelect(full, diags)) return true;

            // 2) 退一步：打开所在文件夹。ShellExecute 会复用已运行的 explorer，不新建进程，
            //    因此不会撞出 0xc0000142 的系统错误框。
            string openTarget = isDir ? full : Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(openTarget) && TryShellExecuteOpen(openTarget, diags)) return true;

            // 3) 可选的最后手段：拉起 explorer.exe /select。能选中，但新建进程，
            //    在装了较多外壳扩展的机器上有概率初始化失败（系统级 0xc0000142 弹框），
            //    所以默认关闭，需要在「设置」里显式打开。
            if (AllowNewExplorerProcess)
            {
                if (TryExplorerSelect(full, diags)) return true;
            }
            else
            {
                diags.Add("explorer /select: disabled (AllowNewExplorerProcess=false)");
            }

            LastDiag = string.Join(" | ", diags.ToArray());
            return false;
        }

        /// <summary>打开目录：先试定位，再试直接打开。</summary>
        internal static bool OpenFolder(string folder)
        {
            return RevealPath(folder);
        }

        /// <summary>打开 URL（http/https）或任意已注册关联的文档，交给 ShellExecute。</summary>
        internal static bool OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = url;
                psi.UseShellExecute = true;
                psi.ErrorDialog = false;
                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        try { p.Dispose(); }
                        catch { }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LastDiag = "OpenUrl: " + ex.GetType().Name + " " + ex.Message;
                return false;
            }
        }
    }
}
