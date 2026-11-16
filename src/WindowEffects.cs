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
    // Va por tiempo (no por cuadros): dura lo mismo aunque la PC este ocupada, y cada cuadro es ligero
    // (una foto chica que se estira; la transparencia la hace Windows).
    class MinimizeFx : Form
    {
        readonly IntPtr target; readonly Rectangle from; Rectangle to;
        Bitmap shot; readonly Timer timer = new Timer { Interval = 10 };
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        const double DurationMs = 300;

        public MinimizeFx(IntPtr h, N.RECT fr)
        {
            target = h;
            from = Rectangle.FromLTRB(fr.left, fr.top, fr.right, fr.bottom);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true; BackColor = Color.Black;
            Bounds = from;
            Opacity = 0.99;   // la hace "transparente por capas" desde el inicio (cambiar la opacidad despues es instantaneo)
            var scr = Screen.FromRectangle(from).Bounds;
            int tw = Math.Max(40, from.Width / 12), th = Math.Max(30, from.Height / 12);
            to = new Rectangle(scr.Left + scr.Width / 2 - tw / 2, scr.Bottom - th - 10, tw, th);   // hacia el dock (abajo al centro)
            timer.Tick += (o, e) => Step();
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x8 | 0x20 | 0x8000000; return cp; } }   // sin taskbar, encima, el mouse pasa, no roba el foco
        protected override bool ShowWithoutActivation { get { return true; } }

        public void Start()
        {
            if (from.Width < 50 || from.Height < 50) { N.PostMessage(target, 0x0112, new IntPtr(0xF020), IntPtr.Zero); Dispose(); return; }
            // foto de la ventana tal como se ve, reducida (ligera para estirarla en cada cuadro)
            float k = Math.Min(1f, 640f / Math.Max(from.Width, from.Height));
            using (var full = new Bitmap(from.Width, from.Height))
            {
                using (var g = Graphics.FromImage(full)) g.CopyFromScreen(from.Location, Point.Empty, from.Size);
                shot = new Bitmap(Math.Max(1, (int)(from.Width * k)), Math.Max(1, (int)(from.Height * k)));
                using (var g = Graphics.FromImage(shot)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(full, 0, 0, shot.Width, shot.Height); }
            }
            Show();
            Refresh();   // la foto ya esta encima antes de que la ventana real se vaya
            WindowEffects.WindowsAnimation(target, false);
            // la ventana real se minimiza ya: se le pide a Windows directo (algunas apps tardan en atender el aviso normal)
            if (!WindowEffects.ShowWindowAsync(target, 6)) N.PostMessage(target, 0x0112, new IntPtr(0xF020), IntPtr.Zero);
            clock.Start();
            timer.Start();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (shot == null) return;
            e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            e.Graphics.DrawImage(shot, ClientRectangle);
        }

        void Step()
        {
            double t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / DurationMs);
            float e = (float)(t * t);                                   // empieza lento y acelera (cae)
            float ew = (float)Math.Pow(t, 0.75), eh = (float)Math.Pow(t, 1.35);   // se angosta antes de achatarse
            float cxF = from.Left + from.Width / 2f, cxT = to.Left + to.Width / 2f;
            float w = from.Width + (to.Width - from.Width) * ew, hgt = from.Height + (to.Height - from.Height) * eh;
            float cx = cxF + (cxT - cxF) * e, bottom = from.Bottom + (to.Bottom - from.Bottom) * e;
            var r = Rectangle.Round(new RectangleF(cx - w / 2, bottom - hgt, Math.Max(2, w), Math.Max(2, hgt)));
            Bounds = r;
            Opacity = t < 0.65 ? 0.99 : Math.Max(0.0, 0.99 * (1 - (t - 0.65) / 0.35));
            if (t >= 1.0)
            {
                timer.Stop();
                var h = target;
                var back = new Timer { Interval = 150 };   // la animacion de Windows vuelve para cuando la restauren
                back.Tick += (o, ev) =>
                {
                    back.Stop(); back.Dispose();
                    if (!N.IsIconic(h)) WindowEffects.ShowWindowAsync(h, 6);   // por si la app ignoro el aviso: minimizar de otra forma
                    WindowEffects.WindowsAnimation(h, true);
                };
                back.Start();
                Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (shot != null) { shot.Dispose(); shot = null; } }
            base.Dispose(disposing);
        }
    }
}
