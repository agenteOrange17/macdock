using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MacDock
{
    // "Mis imágenes": tus propias imagenes como fondo, una para cada momento del dia
    // (amanecer, dia, atardecer, noche). Si eliges solo una, se usa todo el dia.
    // Las imagenes se quedan donde las tienes; MacDock solo guarda la ruta y hace una copia del tamaño de tu pantalla.
    static partial class Wallpapers
    {
        public static readonly Dictionary<string, string> MyFiles = new Dictionary<string, string>();
        public static bool MyPixelArt = true;   // agrandar sin difuminar (para que el pixel art se vea nitido)
        static string MyListFile { get { return Path.Combine(Program.DataDir, "mis-fondos.txt"); } }

        public static void LoadMine()
        {
            MyFiles.Clear();
            try
            {
                if (!File.Exists(MyListFile)) return;
                foreach (var line in File.ReadAllLines(MyListFile))
                {
                    int eq = line.IndexOf('='); if (eq < 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    if (k == "pixel") MyPixelArt = v == "1";
                    else if (Array.IndexOf(Periods, k) >= 0 && v != "") MyFiles[k] = v;
                }
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        public static void SaveMine()
        {
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                var lines = new List<string> { "pixel=" + (MyPixelArt ? 1 : 0) };
                foreach (var kv in MyFiles) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(MyListFile, lines.ToArray());
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        // la imagen elegida para ese momento, o la mas cercana si no eligieron una para el
        static string MineFor(string period)
        {
            string f;
            if (MyFiles.TryGetValue(period, out f) && File.Exists(f)) return f;
            string[] order = period == "noche" ? new[] { "atardecer", "amanecer", "dia" } : period == "atardecer" ? new[] { "noche", "dia", "amanecer" } :
                             period == "amanecer" ? new[] { "dia", "atardecer", "noche" } : new[] { "amanecer", "atardecer", "noche" };
            foreach (var p in order) if (MyFiles.TryGetValue(p, out f) && File.Exists(f)) return f;
            return null;
        }

        public static void ApplyMine(Action<bool> done)
        {
            var th = new Thread(() =>
            {
                bool ok = false;
                try
                {
                    RememberPrevious();
                    Directory.CreateDirectory(PicDir);
                    var b = Screen.PrimaryScreen.Bounds;
                    foreach (var p in Periods)
                    {
                        string src = MineFor(p); if (src == null) continue;
                        using (var bmp = FitToScreen(src, b.Width, b.Height, MyPixelArt)) bmp.Save(PeriodFile(p), ImageFormat.Png);
                    }
                    if (File.Exists(PeriodFile(PeriodNow()))) { SetWallpaper(PeriodFile(PeriodNow())); ok = true; }
                }
                catch (Exception ex) { Program.Log(ex); }
                if (done != null) done(ok);
            });
            th.IsBackground = true; th.Start();
        }

        // recorta para llenar la pantalla sin deformar; el pixel art se agranda sin difuminar
        static Bitmap FitToScreen(string file, int W, int H, bool pixel)
        {
            using (var src = LoadImage(file))
            {
                var outBmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                float scale = Math.Max((float)W / src.Width, (float)H / src.Height);
                if (pixel && scale >= 1f) scale = Math.Max((float)Math.Ceiling(scale - 0.15f), scale);   // pixeles parejos cuando se puede
                float dw = src.Width * scale, dh = src.Height * scale;
                using (var g = Graphics.FromImage(outBmp))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = pixel ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = pixel ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new RectangleF((W - dw) / 2, (H - dh) / 2, dw, dh));
                }
                return outBmp;
            }
        }

        // abre la imagen sin dejar el archivo bloqueado (y toma el primer cuadro si es GIF)
        public static Bitmap LoadImage(string file)
        {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var img = Image.FromStream(fs))
                return new Bitmap(img);
        }
    }

    // Ventana "Mis imágenes": cuatro espacios (amanecer, dia, atardecer, noche) para poner tus fondos.
    class MyWallpapersWindow : MacWindow
    {
        static MyWallpapersWindow current;
        readonly Action applied;
        readonly Dictionary<string, Bitmap> thumbs = new Dictionary<string, Bitmap>();
        bool busy; string note = "";

        static readonly string[] Labels = { "Amanecer", "Día", "Atardecer", "Noche" };
        static readonly string[] Hours = { "6:00 a 9:00", "9:00 a 17:00", "17:00 a 19:30", "19:30 a 6:00" };

        public static void Open(Action onApplied)
        {
            try
            {
                if (current != null && !current.IsDisposed) { current.Activate(); return; }
                current = new MyWallpapersWindow(onApplied);
                current.FormClosed += (o, e) => { current = null; };
                current.Show();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        MyWallpapersWindow(Action onApplied) : base("Mis imágenes de fondo", 720, 600)
        {
            applied = onApplied;
            Wallpapers.LoadMine();
            foreach (var p in Wallpapers.Periods) LoadThumb(p);
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        void LoadThumb(string period)
        {
            Bitmap old;
            if (thumbs.TryGetValue(period, out old) && old != null) old.Dispose();
            thumbs[period] = null;
            string f;
            if (!Wallpapers.MyFiles.TryGetValue(period, out f) || !File.Exists(f)) return;
            try
            {
                using (var b = Wallpapers.LoadImage(f))
                {
                    float k = Math.Min(1f, 480f / b.Width);
                    var t = new Bitmap(Math.Max(1, (int)(b.Width * k)), Math.Max(1, (int)(b.Height * k)));
                    using (var g = Graphics.FromImage(t)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(b, 0, 0, t.Width, t.Height); }
                    thumbs[period] = t;
                }
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        void Pick(string period)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Elige una imagen", Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Todos los archivos|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { using (Wallpapers.LoadImage(dlg.FileName)) { } }
                catch { note = "Esa imagen no se pudo abrir. Prueba con JPG o PNG."; Invalidate(); return; }
                Wallpapers.MyFiles[period] = dlg.FileName;
                Wallpapers.SaveMine();
                LoadThumb(period);
                note = "";
                Invalidate();
            }
        }

        void Apply()
        {
            if (Wallpapers.MyFiles.Count == 0) { note = "Primero elige al menos una imagen."; Invalidate(); return; }
            busy = true; note = "Poniendo tus fondos…"; Invalidate();
            Wallpapers.ApplyMine(ok =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        busy = false;
                        note = ok ? "Listo. Cambia solo según la hora del día." : "No se pudo poner el fondo. Revisa error.log.";
                        if (ok && applied != null) applied();
                        Invalidate();
                    }));
                }
                catch { }
            });
        }

        protected override void Draw(Graphics g)
        {
            float pad = 16 * s, gap = 12 * s;
            var r = new RectangleF(pad, TitleH + 12 * s, ClientSize.Width - pad * 2, ClientSize.Height - TitleH - 12 * s - pad);
            using (var f = F(12.5f)) Txt(g, "Elige una imagen para cada momento del día. Si pones solo una, se usa todo el día.", f, Sub, new RectangleF(r.X, r.Y, r.Width, 20 * s));

            // cuatro espacios en cuadricula 2x2
            float footH = 96 * s, gy = r.Y + 30 * s;
            float cw = (r.Width - gap) / 2, ch = (r.Bottom - footH - gy - gap) / 2;
            for (int i = 0; i < 4; i++)
            {
                string period = Wallpapers.Periods[i];
                var cr = new RectangleF(r.X + (i % 2) * (cw + gap), gy + (i / 2) * (ch + gap), cw, ch);
                Card(g, cr);
                var img = new RectangleF(cr.X + 10 * s, cr.Y + 10 * s, cr.Width - 20 * s, cr.Height - 54 * s);
                Bitmap t; thumbs.TryGetValue(period, out t);
                using (var p = Round(img, 10 * s))
                {
                    var st = g.Save(); g.SetClip(p);
                    if (t != null)
                    {
                        float k = Math.Max(img.Width / t.Width, img.Height / t.Height), dw = t.Width * k, dh = t.Height * k;
                        g.InterpolationMode = Wallpapers.MyPixelArt ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                        g.DrawImage(t, new RectangleF(img.X + (img.Width - dw) / 2, img.Y + (img.Height - dh) / 2, dw, dh));
                    }
                    else
                    {
                        using (var b = new SolidBrush(Hover(img) ? (light ? Color.FromArgb(214, 214, 224) : Color.FromArgb(72, 72, 84)) : Soft)) g.FillPath(b, p);
                        Glyph(g, "plus", new RectangleF(img.X + img.Width / 2 - 14 * s, img.Y + img.Height / 2 - 24 * s, 28 * s, 28 * s), Sub, 2f);
                        using (var f = F(12f)) Txt(g, "Elegir imagen", f, Sub, new RectangleF(img.X, img.Y + img.Height / 2 + 6 * s, img.Width, 18 * s), StringAlignment.Center);
                    }
                    g.Restore(st);
                }
                string pp = period;
                Hit(img, () => Pick(pp));
                using (var f = F(13.5f, true)) Txt(g, Labels[i], f, Fg, new RectangleF(cr.X + 14 * s, img.Bottom + 8 * s, cw / 2, 18 * s));
                using (var f = F(11.5f)) Txt(g, Hours[i], f, Sub, new RectangleF(cr.X + 14 * s, img.Bottom + 25 * s, cw / 2, 16 * s));
                if (t != null)
                {
                    var del = new RectangleF(cr.Right - 14 * s - 70 * s, img.Bottom + 10 * s, 70 * s, 26 * s);
                    Button(g, del, "Quitar", () => { Wallpapers.MyFiles.Remove(pp); Wallpapers.SaveMine(); LoadThumb(pp); });
                    var chg = new RectangleF(del.X - 8 * s - 76 * s, del.Y, 76 * s, 26 * s);
                    Button(g, chg, "Cambiar", () => Pick(pp));
                }
            }

            // abajo: pixel art nitido y boton para aplicar
            var foot = new RectangleF(r.X, r.Bottom - footH + 12 * s, r.Width, footH - 12 * s);
            Card(g, foot);
            var row = new RectangleF(foot.X + 16 * s, foot.Y + 8 * s, foot.Width - 32 * s, 36 * s);
            using (var f = F(13f)) Txt(g, "Mantener los pixeles nítidos (para pixel art)", f, Fg, new RectangleF(row.X, row.Y, row.Width - 200 * s, row.Height));
            Switch(g, new RectangleF(row.X + row.Width - 200 * s - 50 * s, row.Y + 7 * s, 40 * s, 22 * s), Wallpapers.MyPixelArt,
                () => { Wallpapers.MyPixelArt = !Wallpapers.MyPixelArt; Wallpapers.SaveMine(); });
            Button(g, new RectangleF(row.Right - 180 * s, row.Y + 2 * s, 180 * s, 32 * s), busy ? "Un momento…" : "Usar mis imágenes", Apply, true, null, !busy);
            using (var f = F(11.5f)) Txt(g, note != "" ? note : "Tus imágenes se quedan en su carpeta; si las mueves, vuelve a elegirlas.", f, note.StartsWith("Listo") ? Green : Sub,
                new RectangleF(row.X, row.Bottom + 2 * s, row.Width, 18 * s));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            foreach (var t in thumbs.Values) if (t != null) t.Dispose();
            base.OnFormClosed(e);
        }
    }
}
