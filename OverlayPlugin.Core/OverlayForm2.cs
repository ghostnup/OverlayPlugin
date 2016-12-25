using RainbowMage.HtmlRenderer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xilium.CefGlue;

namespace RainbowMage.OverlayPlugin
{
    public partial class OverlayForm2 : Form
    {
        private DIBitmap surfaceBuffer;
        private object surfaceBufferLocker = new object();
        private int maxFrameRate;
        private bool terminated = false;

        public Renderer Renderer { get; private set; }

        private string url;
        public string Url
        {
            get { return this.url; }
            set
            {
                this.url = value;
                UpdateRender();
            }
        }

        public bool IsLoaded { get; private set; }

        public OverlayForm2(string url)
        {
            InitializeComponent();

            Renderer.Initialize();

            this.maxFrameRate = 10;
            this.Renderer = new Renderer();
            this.Renderer.Render += renderer_Render;

            this.url = url;
        }

        public void Reload()
        {
            this.Renderer.Reload();
        }

        #region Layered window related stuffs
        protected override System.Windows.Forms.CreateParams CreateParams
        {
            get
            {
                const int CP_NOCLOSE_BUTTON = 0x200;

                var cp = base.CreateParams;
                //cp.ClassStyle = cp.ClassStyle | CP_NOCLOSE_BUTTON;

                return cp;
            }
        }

        private void UpdateLayeredWindowBitmap()
        {
            if (surfaceBuffer.IsDisposed || this.terminated) { return; }

            using (var gScreen = Graphics.FromHwnd(IntPtr.Zero))
            {
                var currentContext = BufferedGraphicsManager.Current;
                using (var myBuffer = currentContext.Allocate(gScreen, this.DisplayRectangle))
                {
                    var g = myBuffer.Graphics;

                    var pMemory = g.GetHdc();

                    var rect = new NativeMethods.RECT(0, 0, this.Width, this.Height);
                    var brush = NativeMethods.CreateSolidBrush((uint)ColorTranslator.ToWin32(this.BackColor));
                    
                    NativeMethods.FillRect(pMemory, ref rect, brush);
                    NativeMethods.DeleteObject(brush);

                    var pOrig = NativeMethods.SelectObject(surfaceBuffer.DeviceContext, surfaceBuffer.Handle);
                    NativeMethods.AlphaBlend(pMemory, 0, 0, surfaceBuffer.Width, surfaceBuffer.Height, surfaceBuffer.DeviceContext, 0, 0, surfaceBuffer.Width, surfaceBuffer.Height, new NativeMethods.BLENDFUNCTION(NativeMethods.AC_SRC_OVER, 0, 255, NativeMethods.AC_SRC_ALPHA));

                    IntPtr handle = IntPtr.Zero;
                    try
                    {
                        if (!this.terminated)
                        {
                            if (this.InvokeRequired)
                            {
                                this.Invoke(new Action(() =>
                                {
                                    handle = this.Handle;
                                }));
                            }
                            else
                            {
                                handle = this.Handle;
                            }

                            var gr = Graphics.FromHwnd(handle);
                            IntPtr pTarget = gr.GetHdc();
                            NativeMethods.BitBlt(pTarget, 0, 0, this.DisplayRectangle.Width, this.DisplayRectangle.Height, pMemory, 0, 0, NativeMethods.TernaryRasterOperations.SRCCOPY);
                            gr.ReleaseHdc(pTarget);
                            NativeMethods.DeleteDC(pTarget);
                            gr.Dispose();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    NativeMethods.SelectObject(surfaceBuffer.DeviceContext, pOrig);
                    g.ReleaseHdc(pMemory);
                    NativeMethods.DeleteDC(pMemory);
                    g.Dispose();
                }
            }
        }
        #endregion

        void renderer_Render(object sender, RenderEventArgs e)
        {
            if (!this.terminated)
            {
                try
                {
                    if (surfaceBuffer != null &&
                        (surfaceBuffer.Width != e.Width || surfaceBuffer.Height != e.Height))
                    {
                        surfaceBuffer.Dispose();
                        surfaceBuffer = null;
                    }

                    if (surfaceBuffer == null)
                    {
                        surfaceBuffer = new DIBitmap(e.Width, e.Height);
                    }

                    // TODO: DirtyRect に対応
                    surfaceBuffer.SetSurfaceData(e.Buffer, (uint)(e.Width * e.Height * 4));

                    UpdateLayeredWindowBitmap();
                }
                catch
                {

                }
            }
        }

        private void UpdateRender()
        {
            if (this.Renderer != null)
            {
                this.Renderer.BeginRender(this.Width - 15 , this.Height - 15, this.Url, this.maxFrameRate);
            }
        }

        private void OverlayForm2_Load(object sender, EventArgs e)
        {
            this.IsLoaded = true;

            UpdateRender();
        }

        private void OverlayForm2_VisibleChanged(object sender, EventArgs e)
        {
            if (this.Visible == true && this.Renderer != null)
            {
                this.Renderer.Reload();
            }
        }

        private void OverlayForm2_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.WindowState = FormWindowState.Minimized; 
        }

        private void OverlayForm2_FormClosed(object sender, FormClosedEventArgs e)
        {
            this.Renderer.EndRender();
            terminated = true;
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (this.Renderer != null)
            {
                this.Renderer.Dispose();
                this.Renderer = null;
            }

            if (this.surfaceBuffer != null)
            {
                this.surfaceBuffer.Dispose();
            }

            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OverlayForm2_Resize(object sender, EventArgs e)
        {
            if (this.Renderer != null)
            {
                this.Renderer.Resize(this.Width - 15, this.Height - 15);
            }
        }

        private void OverlayForm2_MouseDown(object sender, MouseEventArgs e)
        {
            this.Renderer.SendMouseUpDown(e.X, e.Y, GetMouseButtonType(e), false);
        }

        private void OverlayForm2_MouseMove(object sender, MouseEventArgs e)
        {
                this.Renderer.SendMouseMove(e.X, e.Y, GetMouseButtonType(e));
        }

        private void OverlayForm2_MouseUp(object sender, MouseEventArgs e)
        {
            this.Renderer.SendMouseUpDown(e.X, e.Y, GetMouseButtonType(e), true);
        }

        private CefMouseButtonType GetMouseButtonType(MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                return Xilium.CefGlue.CefMouseButtonType.Left;
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Middle)
            {
                return Xilium.CefGlue.CefMouseButtonType.Middle;
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                return Xilium.CefGlue.CefMouseButtonType.Right;
            }
            else
            {
                return CefMouseButtonType.Left; // 非対応のボタンは左クリックとして扱う
            }
        }

        private CefEventFlags GetMouseEventFlags(MouseEventArgs e)
        {
            var flags = CefEventFlags.None;

            if (e.Button == MouseButtons.Left)
            {
                flags |= CefEventFlags.LeftMouseButton;
            }
            else if (e.Button == MouseButtons.Middle)
            {
                flags |= CefEventFlags.MiddleMouseButton;
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                flags |= CefEventFlags.RightMouseButton;
            }

            return flags;
        }

        private bool IsOverlaysGameWindow()
        {
            var xivHandle = GetGameWindowHandle();
            var handle = this.Handle;

            while (handle != IntPtr.Zero)
            {
                // Overlayウィンドウよりも前面側にFF14のウィンドウがあった
                if (handle == xivHandle)
                {
                    return false;
                }

                handle = NativeMethods.GetWindow(handle, NativeMethods.GW_HWNDPREV);
            }

            // 前面側にOverlayが存在する、もしくはFF14が起動していない
            return true;
        }

        private static object xivProcLocker = new object();
        private static Process xivProc;
        private static DateTime lastTry;
        private static TimeSpan tryInterval = new TimeSpan(0, 0, 15);

        private static IntPtr GetGameWindowHandle()
        {
            lock (xivProcLocker)
            {
                // プロセスがすでに終了してるならプロセス情報をクリア
                if (xivProc != null && xivProc.HasExited)
                {
                    xivProc = null;
                }

                // プロセス情報がなく、tryIntervalよりも時間が経っているときは新たに取得を試みる
                if (xivProc == null && DateTime.Now - lastTry > tryInterval)
                {
                    xivProc = Process.GetProcessesByName("ffxiv").FirstOrDefault();
                    if (xivProc == null)
                    {
                        xivProc = Process.GetProcessesByName("ffxiv_dx11").FirstOrDefault();
                    }
                    lastTry = DateTime.Now;
                }

                if (xivProc != null)
                {
                    return xivProc.MainWindowHandle;
                }
                else
                {
                    return IntPtr.Zero;
                }
            }
        }        
    }
}
