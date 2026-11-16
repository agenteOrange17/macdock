using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacDock
{
    // Cosas de Mac para las ventanas de otras apps (usadas por los botones de Mac):
    //  - acomodar: llenar la pantalla, centrar, mitad izquierda / derecha (menu del boton verde)
    //  - minimizar con animacion: la ventana "cae" hacia el dock
    // No se mete dentro de las apps: solo usa lo que Windows permite (mover, minimizar, una imagen de la pantalla).
    static class WindowEffects
    {
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out N.RECT r, int size);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int cmd);

        // la ultima vez que MacDock agrando una ventana (para no confundirlo con un doble clic en la barra)
        public static IntPtr LastZoomHwnd; public static int LastZoomTick;

        static N.RECT Frame(IntPtr h)
        {
            N.RECT fr; if (DwmGetWindowAttribute(h, 9, out fr, 16) != 0) N.GetWindowRect(h, out fr);
            return fr;
        }

        public static void Maximize(IntPtr h)
        {
            LastZoomHwnd = h; LastZoomTick = Environment.TickCount;
            N.PostMessage(h, 0x0112, new IntPtr(0xF030), IntPtr.Zero);
        }

        // modo: 0 llenar, 1 centrar, 2 mitad izquierda, 3 mitad derecha
        public static void Arrange(IntPtr h, int mode)
        {
            try
            {
                if (mode == 0) { Maximize(h); return; }
                if (N.IsZoomed(h) || N.IsIconic(h)) { N.ShowWindow(h, 9); System.Threading.Thread.Sleep(60); }
                var wa = Screen.FromHandle(h).WorkingArea;
                N.RECT fr = Frame(h), wr; N.GetWindowRect(h, out wr);
                // Windows deja un borde invisible alrededor; se compensa para que no queden huecos
                int bl = fr.left - wr.left, bt = fr.top - wr.top, br = wr.right - fr.right, bb = wr.bottom - fr.bottom;
                int w = fr.right - fr.left, hh = fr.bottom - fr.top;
                Rectangle r;
                if (mode == 1) { w = Math.Min(w, wa.Width); hh = Math.Min(hh, wa.Height); r = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - hh) / 2, w, hh); }
                else if (mode == 2) r = new Rectangle(wa.Left, wa.Top, wa.Width / 2, wa.Height);
                else r = new Rectangle(wa.Left + wa.Width / 2, wa.Top, wa.Width - wa.Width / 2, wa.Height);
                N.SetWindowPos(h, IntPtr.Zero, r.Left - bl, r.Top - bt, r.Width + bl + br, r.Height + bt + bb, 0x0004 | 0x0010);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        // menu del boton verde (como en macOS al dejar el mouse encima)
        public static void ShowArrangeMenu(IntPtr h, Point screenAt, bool light)
        {
            var m = MenuStyle.New(light);
            Action<string, int> add = (t, mode) => { var mi = new ToolStripMenuItem(t); mi.Click += (o, e) => Arrange(h, mode); m.Items.Add(mi); };
            add("Llenar la pantalla", 0);
            add("Centrar", 1);
            m.Items.Add(new ToolStripSeparator());
            add("Mover a la mitad izquierda", 2);
            add("Mover a la mitad derecha", 3);
            m.Show(screenAt);
        }

        // ---------- minimizar con animacion hacia el dock ----------
        public static void Minimize(IntPtr h, bool animate)
        {
            if (!animate || N.IsIconic(h)) { N.PostMessage(h, 0x0112, new IntPtr(0xF020), IntPtr.Zero); return; }
            try { new MinimizeFx(h, Frame(h)).Start(); }
            catch (Exception ex) { Program.Log(ex); N.PostMessage(h, 0x0112, new IntPtr(0xF020), IntPtr.Zero); }
        }

        // apaga/prende la animacion propia de Windows para esa ventana (para que no se encimen las dos)
        public static void WindowsAnimation(IntPtr h, bool on)
        {
            try { int v = on ? 0 : 1; DwmSetWindowAttribute(h, 3, ref v, 4); } catch { }
        }
    }

    // La "foto" de la ventana que se encoge y cae hacia el dock mientras la ventana real ya se minimizo.
    // Se dibuja directo con Windows (sin el pintado de WinForms), asi aparece ya con la foto puesta: sin parpadeo.
    // Mientras dura, se apaga un momento la animacion de minimizar de Windows para que no se encimen las dos.
    class MinimizeFx : Form
    {
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
        [StructLayout(LayoutKind.Sequential)] struct ANIMATIONINFO { public int cbSize; public int iMinAnimate; }
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SpiAnim(int a, int u, ref ANIMATIONINFO p, int f);

        readonly IntPtr target; readonly Rectangle from; Rectangle to;
        readonly Timer timer = new Timer { Interval = 10 };
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        const double DurationMs = 300;
        IntPtr srcDc, srcBmp, srcOld, dstDc, dstBmp, dstOld;   // foto original y lienzo
        static int animOff;   // cuantas animaciones nuestras estan corriendo (para regresar la de Windows al final)
        static bool restoreWinAnim;

        public MinimizeFx(IntPtr h, N.RECT fr)
        {
            target = h;
            from = Rectangle.FromLTRB(fr.left, fr.top, fr.right, fr.bottom);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(from.Left, from.Top, 1, 1);
            var scr = Screen.FromRectangle(from).Bounds;
            int tw = Math.Max(40, from.Width / 12), th = Math.Max(30, from.Height / 12);
            to = new Rectangle(scr.Left + scr.Width / 2 - tw / 2, scr.Bottom - th - 10, tw, th);   // hacia el dock (abajo al centro)
            timer.Tick += (o, e) => Step();
            CreateHandle();
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x20 | 0x8000000; return cp; } }
        protected override bool ShowWithoutActivation { get { return true; } }

        public void Start()
        {
            if (from.Width < 50 || from.Height < 50) { WindowEffects.ShowWindowAsync(target, 6); Dispose(); return; }
            IntPtr scr = N.GetDC(IntPtr.Zero);
            try
            {
                // foto de la ventana a tamaño real (un solo copiado de pantalla, rapidisimo)
                srcDc = N.CreateCompatibleDC(scr); srcBmp = CreateCompatibleBitmap(scr, from.Width, from.Height); srcOld = N.SelectObject(srcDc, srcBmp);
                BitBlt(srcDc, 0, 0, from.Width, from.Height, scr, from.Left, from.Top, 0x00CC0020);
                dstDc = N.CreateCompatibleDC(scr); dstBmp = CreateCompatibleBitmap(scr, from.Width, from.Height); dstOld = N.SelectObject(dstDc, dstBmp);
                SetStretchBltMode(dstDc, 3);
            }
            finally { N.ReleaseDC(IntPtr.Zero, scr); }

            Render(0);                      // la foto queda puesta antes de mostrarse...
            N.ShowWindow(Handle, 4);        // ...y aparece encima de la ventana, identica
            WinAnimation(false);            // que Windows no haga su propia animacion encima de la nuestra
            if (!WindowEffects.ShowWindowAsync(target, 6)) N.PostMessage(target, 0x0112, new IntPtr(0xF020), IntPtr.Zero);
            clock.Start(); timer.Start();
        }

        static void WinAnimation(bool on)
        {
            try
            {
                var ai = new ANIMATIONINFO { cbSize = 8 };
                if (!on)
                {
                    animOff++;
                    if (animOff > 1) return;
                    SpiAnim(0x0048, 8, ref ai, 0);                     // SPI_GETANIMATION
                    restoreWinAnim = ai.iMinAnimate != 0;
                    if (restoreWinAnim) { ai.iMinAnimate = 0; SpiAnim(0x0049, 8, ref ai, 0); }   // solo por este momento, no se guarda
                }
                else
                {
                    animOff = Math.Max(0, animOff - 1);
                    if (animOff > 0 || !restoreWinAnim) return;
                    ai.iMinAnimate = 1; SpiAnim(0x0049, 8, ref ai, 0);
                    restoreWinAnim = false;
                }
            }
            catch { }
        }

        void Render(double t)
        {
            float e = (float)(t * t);                                               // empieza lento y acelera (cae)
            float ew = (float)Math.Pow(t, 0.75), eh = (float)Math.Pow(t, 1.35);     // se angosta antes de achatarse
            float cxF = from.Left + from.Width / 2f, cxT = to.Left + to.Width / 2f;
            float w = from.Width + (to.Width - from.Width) * ew, hgt = from.Height + (to.Height - from.Height) * eh;
            float cx = cxF + (cxT - cxF) * e, bottom = from.Bottom + (to.Bottom - from.Bottom) * e;
            var r = Rectangle.Round(new RectangleF(cx - w / 2, bottom - hgt, Math.Max(2, w), Math.Max(2, hgt)));
            if (t <= 0) BitBlt(dstDc, 0, 0, from.Width, from.Height, srcDc, 0, 0, 0x00CC0020);
            else StretchBlt(dstDc, 0, 0, r.Width, r.Height, srcDc, 0, 0, from.Width, from.Height, 0x00CC0020);
            byte alpha = (byte)(255 * (t < 0.65 ? 1.0 : Math.Max(0.0, 1 - (t - 0.65) / 0.35)));
            IntPtr scr = N.GetDC(IntPtr.Zero);
            try
            {
                var size = new N.SIZE { cx = r.Width, cy = r.Height }; var src = new N.POINT(); var dst = new N.POINT { x = r.Left, y = r.Top };
                var bl = new N.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 0 };
                N.UpdateLayeredWindow(Handle, scr, ref dst, ref size, dstDc, ref src, 0, ref bl, 2);
            }
            finally { N.ReleaseDC(IntPtr.Zero, scr); }
        }

        void Step()
        {
            double t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / DurationMs);
            try { Render(t); } catch (Exception ex) { Program.Log(ex); t = 1; }
            if (t >= 1.0)
            {
                timer.Stop();
                var h = target;
                var back = new Timer { Interval = 120 };
                back.Tick += (o, ev) =>
                {
                    back.Stop(); back.Dispose();
                    if (!N.IsIconic(h)) WindowEffects.ShowWindowAsync(h, 6);   // por si la app no hizo caso
                    WinAnimation(true);                                         // la animacion de Windows regresa como estaba
                };
                back.Start();
                Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            if (srcDc != IntPtr.Zero) { N.SelectObject(srcDc, srcOld); N.DeleteObject(srcBmp); N.DeleteDC(srcDc); srcDc = IntPtr.Zero; }
            if (dstDc != IntPtr.Zero) { N.SelectObject(dstDc, dstOld); N.DeleteObject(dstBmp); N.DeleteDC(dstDc); dstDc = IntPtr.Zero; }
            base.Dispose(disposing);
        }
    }
}
