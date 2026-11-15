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
    class MinimizeFx : Form
    {
        readonly IntPtr target; readonly Rectangle from; Rectangle to;
        Bitmap shot; readonly Timer timer = new Timer { Interval = 15 };
        int frame; const int Frames = 18;

        public MinimizeFx(IntPtr h, N.RECT fr)
        {
            target = h;
            from = Rectangle.FromLTRB(fr.left, fr.top, fr.right, fr.bottom);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(-200, -200, 1, 1);
            var scr = Screen.FromRectangle(from).Bounds;
            int tw = Math.Max(40, from.Width / 12), th = Math.Max(30, from.Height / 12);
            to = new Rectangle(scr.Left + scr.Width / 2 - tw / 2, scr.Bottom - th - 10, tw, th);   // hacia el dock (abajo al centro)
            timer.Tick += (o, e) => Step();
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x20 | 0x8000000; return cp; } }
        protected override bool ShowWithoutActivation { get { return true; } }

        public void Start()
        {
            if (from.Width < 50 || from.Height < 50) { N.PostMessage(target, 0x0112, new IntPtr(0xF020), IntPtr.Zero); Dispose(); return; }
            // foto de la ventana tal como se ve (achicada si es muy grande, para que sea ligera)
            float k = Math.Min(1f, 900f / Math.Max(from.Width, from.Height));
            using (var full = new Bitmap(from.Width, from.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(full)) g.CopyFromScreen(from.Location, Point.Empty, from.Size);
                shot = new Bitmap(Math.Max(1, (int)(from.Width * k)), Math.Max(1, (int)(from.Height * k)), PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(shot)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(full, 0, 0, shot.Width, shot.Height); }
            }
            Show();
            Render(0f);
            WindowEffects.WindowsAnimation(target, false);
            N.PostMessage(target, 0x0112, new IntPtr(0xF020), IntPtr.Zero);
            timer.Start();
        }

        void Step()
        {
            frame++;
            float t = Math.Min(1f, frame / (float)Frames);
            Render(t);
            if (t >= 1f)
            {
                timer.Stop();
                var h = target;
                var back = new Timer { Interval = 400 };   // la animacion de Windows vuelve para cuando la restauren
                back.Tick += (o, e) => { back.Stop(); back.Dispose(); WindowEffects.WindowsAnimation(h, true); };
                back.Start();
                Close();
            }
        }

        // t de 0 a 1: se encoge, se angosta mas rapido de lo ancho que de lo alto (efecto de "caer") y se desvanece al final
        void Render(float t)
        {
            float e = t * t;                                   // empieza lento y acelera
            float ew = (float)Math.Pow(t, 0.75), eh = (float)Math.Pow(t, 1.35);
            float cxF = from.Left + from.Width / 2f, cxT = to.Left + to.Width / 2f;
            float w = from.Width + (to.Width - from.Width) * ew, hgt = from.Height + (to.Height - from.Height) * eh;
            float cx = cxF + (cxT - cxF) * e;
            float bottom = from.Bottom + (to.Bottom - from.Bottom) * e;
            var r = Rectangle.Round(new RectangleF(cx - w / 2, bottom - hgt, w, hgt));
            if (r.Width < 1 || r.Height < 1) return;
            byte alpha = (byte)(255 * (t < 0.7f ? 1f : Math.Max(0f, 1f - (t - 0.7f) / 0.3f)));
            using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) { g.InterpolationMode = InterpolationMode.Bilinear; g.DrawImage(shot, 0, 0, r.Width, r.Height); }
                IntPtr screenDc = N.GetDC(IntPtr.Zero), memDc = N.CreateCompatibleDC(screenDc), hb = bmp.GetHbitmap(Color.FromArgb(0)), old = N.SelectObject(memDc, hb);
                try
                {
                    var size = new N.SIZE { cx = r.Width, cy = r.Height }; var src = new N.POINT(); var dst = new N.POINT { x = r.Left, y = r.Top };
                    var bl = new N.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
                    N.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bl, 2);
                }
                finally { N.ReleaseDC(IntPtr.Zero, screenDc); N.SelectObject(memDc, old); N.DeleteObject(hb); N.DeleteDC(memDc); }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (shot != null) shot.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
