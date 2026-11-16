using System;
using System.Drawing;

namespace MacDock
{
    // Dos escenas nuevas en pixel art, dibujadas por codigo (originales, cada una con 4 versiones segun la hora):
    //  - "azotea":  ciudad vista desde una azotea, con nubes grandes, un gato en la orilla y ventanas encendidas
    //  - "montana": ciudad al pie de una montaña nevada, con un estanque, arboles y un farol de piedra
    static partial class Wallpapers
    {
        // ======================= AZOTEA =======================
        static void Rooftop(Canvas c, Canvas glow, string period, Random rnd)
        {
            int w = c.W, h = c.H;
            Color top, mid, low, cloudDark, cloudMid, cloudRim, far, midC, near, wall, win, sun; float lights; bool stars = false, moon = false;
            switch (period)
            {
                case "amanecer":
                    top = Rgb(62, 86, 150); mid = Rgb(176, 150, 196); low = Rgb(255, 196, 160);
                    cloudDark = Rgb(96, 88, 140); cloudMid = Rgb(160, 128, 170); cloudRim = Rgb(255, 196, 150);
                    far = Rgb(150, 136, 180); midC = Rgb(96, 84, 130); near = Rgb(52, 44, 76); wall = Rgb(70, 54, 78); win = Rgb(255, 214, 150); sun = Rgb(255, 232, 180); lights = 0.25f; break;
                case "dia":
                    top = Rgb(70, 140, 222); mid = Rgb(132, 186, 240); low = Rgb(206, 230, 250);
                    cloudDark = Rgb(170, 188, 220); cloudMid = Rgb(222, 232, 248); cloudRim = Rgb(255, 255, 255);
                    far = Rgb(150, 176, 210); midC = Rgb(104, 124, 162); near = Rgb(66, 74, 104); wall = Rgb(110, 76, 70); win = Rgb(255, 226, 170); sun = Rgb(255, 252, 230); lights = 0.04f; break;
                case "atardecer":
                    top = Rgb(64, 112, 150); mid = Rgb(150, 116, 160); low = Rgb(250, 120, 120);
                    cloudDark = Rgb(66, 58, 92); cloudMid = Rgb(116, 84, 120); cloudRim = Rgb(246, 120, 128);
                    far = Rgb(136, 104, 150); midC = Rgb(78, 66, 104); near = Rgb(40, 34, 58); wall = Rgb(56, 42, 62); win = Rgb(255, 204, 140); sun = Rgb(255, 210, 160); lights = 0.45f; break;
                default:
                    top = Rgb(10, 14, 40); mid = Rgb(30, 36, 80); low = Rgb(70, 62, 120);
                    cloudDark = Rgb(24, 26, 58); cloudMid = Rgb(42, 46, 90); cloudRim = Rgb(120, 130, 190);
                    far = Rgb(52, 52, 100); midC = Rgb(30, 30, 64); near = Rgb(18, 18, 40); wall = Rgb(30, 24, 44); win = Rgb(255, 196, 120); sun = Rgb(230, 236, 255); lights = 0.8f; stars = true; moon = true; break;
            }
            int horizon = (int)(h * 0.70f);

            // cielo
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / horizon;
                for (int x = 0; x < w; x++)
                    c.Set(x, y, t < 0.55f ? Dither(top, mid, t / 0.55f, x, y, 12) : Dither(mid, low, Math.Min(1f, (t - 0.55f) / 0.45f), x, y, 12));
            }
            if (stars || period == "atardecer")
                for (int i = 0; i < w * h / (stars ? 90 : 400); i++)
                {
                    int x = rnd.Next(w), y = rnd.Next((int)(horizon * 0.55f));
                    int b = 150 + rnd.Next(100); c.Set(x, y, Rgb(b, b, Math.Min(255, b + 20)));
                    if (stars && rnd.Next(14) == 0) { c.Set(x + 1, y, Rgb(b, b, b)); c.Set(x - 1, y, Rgb(b, b, b)); c.Set(x, y + 1, Rgb(b, b, b)); c.Set(x, y - 1, Rgb(b, b, b)); }
                }

            // sol o luna detras de las nubes, bajito
            float sx = w * 0.30f, sy = moon ? h * 0.18f : (period == "dia" ? h * 0.16f : horizon - h * 0.06f), sr = moon ? h * 0.05f : h * 0.07f;
            for (int y = (int)(sy - sr * 4); y <= sy + sr * 4; y++)
                for (int x = (int)(sx - sr * 4); x <= sx + sr * 4; x++)
                {
                    float d = (float)Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy));
                    if (d <= sr) { c.Set(x, y, moon && (x - sx + sr * 0.4f) * (x - sx + sr * 0.4f) + (y - sy - sr * 0.2f) * (y - sy - sr * 0.2f) < sr * sr * 0.5f ? Mix(sun, top, 0.15f) : sun); glow.Blend(x, y, sun, 0.5f); }
                    else if (d < sr * 4) glow.Blend(x, y, sun, 0.22f * (1f - d / (sr * 4)));
                }

            // nubes grandes y alargadas, con orilla iluminada hacia abajo (de donde viene la luz)
            float ox = (float)rnd.NextDouble() * 50, oy = (float)rnd.NextDouble() * 50;
            Func<int, int, float> dens = (x, y) =>
            {
                float band = 1f - Math.Abs((float)y / horizon - 0.32f) * 2.4f;               // mas nubes en el centro del cielo
                float band2 = 1f - Math.Abs((float)y / horizon - 0.72f) * 6f;                // una franja fina cerca del horizonte
                float n = Fbm(x * 0.012f + ox, y * 0.045f + oy);
                return Math.Max(band, band2 * 0.8f) * n;
            };
            for (int y = 0; y < horizon; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = dens(x, y);
                    if (d < 0.36f) continue;
                    int dir = period == "dia" ? -1 : 1;   // de dia la luz viene de arriba; al atardecer/noche, de abajo
                    bool lit = dens(x, y + 3 * dir) < 0.36f || dens(x, y + 2 * dir) < 0.36f || dens(x, y + dir) < 0.36f;
                    bool shade = d > 0.5f;
                    var col = lit ? cloudRim : shade ? cloudDark : Dither(cloudMid, cloudDark, (d - 0.36f) / 0.14f, x, y, 3);
                    c.Set(x, y, col);
                    if (lit && period != "dia") glow.Blend(x, y, cloudRim, 0.18f);
                    else glow.Clear(x, y, 1, 1);
                }

            // ciudad lejana (con agujas) y ciudad media
            int farBase = horizon + 2, midBase = horizon + (int)(h * 0.08f);
            Towers(c, glow, rnd, farBase, 0.06f, 0.20f, 4, 10, Mix(far, low, 0.15f), lights * 0.35f, win, 0.35f);
            c.FillRect(0, farBase, w, h - farBase, Mix(far, low, 0.05f)); glow.Clear(0, farBase, w, h - farBase);
            Towers(c, glow, rnd, midBase, 0.06f, 0.16f, 6, 14, midC, lights * 0.7f, win, 0.12f);
            c.FillRect(0, midBase, w, h - midBase, midC); glow.Clear(0, midBase, w, h - midBase);

            // azoteas cercanas: bajitas, con tinacos y antenas
            int x0 = (int)(w * 0.36f);
            int rx = x0;
            while (rx < w)
            {
                int bw = 14 + rnd.Next(34), bh = (int)(h * (0.02f + 0.16f * (float)Math.Pow(rnd.NextDouble(), 1.3)));
                int ty = h - (int)(h * 0.14f) - bh;
                var col = Mix(near, Rgb(0, 0, 0), (float)rnd.NextDouble() * 0.2f);
                c.FillRect(rx, ty, bw, h - ty, col); glow.Clear(rx, ty, bw, h - ty);
                c.FillRect(rx, ty, bw, 1, Mix(col, low, 0.25f));   // borde de la azotea con luz del cielo
                int what = rnd.Next(5);
                if (what == 0 && bw > 14) WaterTank(c, rx + 3 + rnd.Next(Math.Max(1, bw - 12)), ty, col, low);
                else if (what == 1) { int ax = rx + bw / 2; c.FillRect(ax, ty - 9, 1, 9, col); c.FillRect(ax - 3, ty - 7, 7, 1, col); c.FillRect(ax - 2, ty - 5, 5, 1, col); }
                for (int wy = ty + 4; wy < h - 2; wy += 6)
                    for (int wx = rx + 3; wx < rx + bw - 4; wx += 6)
                        if (rnd.NextDouble() < lights * 0.32 + 0.03) { c.FillRect(wx, wy, 3, 2, win); c.Set(wx + 2, wy + 1, Mix(win, col, 0.4f)); if (rnd.Next(3) == 0) glow.FillRect(wx - 1, wy - 1, 5, 4, win); }
                rx += bw + rnd.Next(3);
            }

            // edificio de enfrente (a la izquierda): ladrillo, cornisa, gato en la orilla y una ventana con luz
            int bx1 = (int)(w * 0.36f), roofY = (int)(h * 0.66f);
            for (int y = roofY; y < h; y++)
                for (int x = 0; x < bx1; x++)
                {
                    bool mortarRow = (y - roofY) % 4 == 0;
                    bool mortarCol = ((x + (((y - roofY) / 4) % 2) * 4) % 8) == 0;
                    var bc = mortarRow || mortarCol ? Mix(wall, Rgb(0, 0, 0), 0.25f) : Mix(wall, Rgb(255, 255, 255), Hash(x / 8, y / 4) * 0.06f);
                    c.Set(x, y, bc);
                }
            glow.Clear(0, roofY, bx1, h - roofY);
            // cornisa con luz arriba
            c.FillRect(0, roofY - 4, bx1 + 3, 4, Mix(wall, Rgb(0, 0, 0), 0.1f));
            c.FillRect(0, roofY - 4, bx1 + 3, 1, Mix(wall, low, 0.45f));
            c.FillRect(bx1 + 2, roofY - 4, 1, 4, Mix(wall, low, 0.3f));
            // sombra de la cornisa sobre el ladrillo
            for (int x = 0; x < bx1; x++) c.Blend(x, roofY, Rgb(0, 0, 0), 0.35f);
            // aire acondicionado y antena de TV
            int acx = (int)(w * 0.04f), acw = 22, ach = 18;
            c.FillRect(acx, roofY - 4 - ach, acw, ach, Mix(wall, Rgb(120, 120, 130), 0.4f));
            for (int k = 3; k < ach - 3; k += 2) c.FillRect(acx + 3, roofY - 4 - ach + k, acw - 6, 1, Mix(wall, Rgb(0, 0, 0), 0.2f));
            c.FillRect(acx, roofY - 4 - ach, acw, 1, Mix(wall, low, 0.5f));
            int tx = acx + 8; c.FillRect(tx, roofY - 4 - ach - 30, 1, 30, Mix(wall, Rgb(0, 0, 0), 0.3f));
            for (int k = 0; k < 5; k++) { int len = 12 - k * 2; c.FillRect(tx - len / 2, roofY - 4 - ach - 28 + k * 4, len, 1, Mix(wall, Rgb(0, 0, 0), 0.3f)); }
            // el gato, sentado en la orilla mirando la ciudad
            Cat(c, bx1 - 16, roofY - 4, Mix(near, Rgb(0, 0, 0), 0.6f));
            // ventana con luz calida
            int wx0 = (int)(w * 0.14f), wy0 = roofY + (int)(h * 0.12f), ww = 34, wh = h - wy0 + 2;
            c.FillRect(wx0 - 2, wy0 - 2, ww + 4, wh, Mix(wall, Rgb(0, 0, 0), 0.4f));
            for (int y = wy0; y < h; y++) for (int x = wx0; x < wx0 + ww; x++) c.Set(x, y, Dither(Mix(win, Rgb(255, 255, 255), 0.25f), win, (float)(y - wy0) / Math.Max(1, wh), x, y, 4));
            c.FillRect(wx0 + ww / 2 - 1, wy0, 2, wh, Mix(wall, Rgb(0, 0, 0), 0.4f));
            c.FillRect(wx0, wy0 + 12, ww, 2, Mix(wall, Rgb(0, 0, 0), 0.4f));
            glow.FillRect(wx0 - 6, wy0 - 6, ww + 12, wh + 6, win);
        }

        // torres: con escalones, agujas y ventanitas
        static void Towers(Canvas c, Canvas glow, Random rnd, int baseY, float minH, float maxH, int minW, int maxW, Color body, float lights, Color win, float spire)
        {
            int w = c.W, h = c.H, x = -rnd.Next(maxW);
            while (x < w)
            {
                int bw = minW + rnd.Next(maxW - minW + 1), bh = (int)(h * (minH + (maxH - minH) * (float)Math.Pow(rnd.NextDouble(), 1.6)));
                int top = baseY - bh; var col = Mix(body, Rgb(0, 0, 0), (float)rnd.NextDouble() * 0.15f);
                c.FillRect(x, top, bw, bh, col); glow.Clear(x, top, bw, bh);
                if (rnd.NextDouble() < spire && bh > h * 0.1f)
                {   // escalones y aguja (rascacielos clasico)
                    int s1 = Math.Max(2, bw * 2 / 3), s2 = Math.Max(1, bw / 3), sh = 2 + rnd.Next(4);
                    c.FillRect(x + (bw - s1) / 2, top - sh, s1, sh, col);
                    c.FillRect(x + (bw - s2) / 2, top - sh * 2, s2, sh, col);
                    int nx = x + bw / 2; c.FillRect(nx, top - sh * 2 - 8 - rnd.Next(10), 1, 8 + 10, col);
                }
                c.FillRect(x, top, 1, bh, Mix(col, Rgb(255, 255, 255), 0.06f));
                for (int wy = top + 2; wy < baseY - 1; wy += 3)
                    for (int wx = x + 1; wx < x + bw - 1; wx += 2)
                        if (rnd.NextDouble() < lights * 0.5) { var wc = Mix(win, col, (float)rnd.NextDouble() * 0.45f); c.Set(wx, wy, wc); if (rnd.Next(5) == 0) glow.Blend(wx, wy, wc, 0.35f); }
                x += bw;
            }
        }

        static void WaterTank(Canvas c, int x, int roof, Color col, Color light)
        {
            var dark = Mix(col, Rgb(0, 0, 0), 0.2f);
            c.FillRect(x + 1, roof - 4, 1, 4, dark); c.FillRect(x + 7, roof - 4, 1, 4, dark);   // patas
            c.FillRect(x, roof - 12, 9, 8, col);                                               // tanque
            c.FillRect(x + 1, roof - 14, 7, 2, col); c.FillRect(x + 3, roof - 15, 3, 1, col);   // techito conico
            c.FillRect(x, roof - 12, 1, 8, Mix(col, light, 0.25f));
            c.FillRect(x, roof - 9, 9, 1, dark); c.FillRect(x, roof - 6, 9, 1, dark);           // aros
        }

        // gato sentado de espaldas, con la cola colgando por la orilla
        static void Cat(Canvas c, int x, int ledgeY, Color col)
        {
            string[] sprite =
            {
                "..X....X..",
                "..XX..XX..",
                "..XXXXXX..",
                "..XXXXXX..",
                "..XXXXXX..",
                "...XXXX...",
                "..XXXXXX..",
                ".XXXXXXXX.",
                ".XXXXXXXX.",
                "XXXXXXXXXX",
                "XXXXXXXXXX",
                "XXXXXXXXXX",
                "XXXXXXXXXX",
                ".XXXXXXXX.",
            };
            int sh = sprite.Length;
            for (int y = 0; y < sh; y++)
                for (int k = 0; k < sprite[y].Length; k++)
                    if (sprite[y][k] == 'X') c.Set(x + k, ledgeY - sh + y, col);
            // cola colgando
            int[] tail = { 0, 1, 1, 1, 2, 2, 2, 1, 1, 0 };
            for (int i = 0; i < tail.Length; i++) { c.Set(x + 9 + tail[i], ledgeY + i, col); c.Set(x + 10 + tail[i], ledgeY + i, col); }
        }

        // ======================= MONTAÑA =======================
        static void Mountain(Canvas c, Canvas glow, string period, Random rnd)
        {
            int w = c.W, h = c.H;
            Color top, low, mtn, snow, snowShade, city, cityDark, win, tree, treeDark, ground, water, maple, mapleDark, lamp; float lights; bool stars = false, lampOn;
            switch (period)
            {
                case "amanecer":
                    top = Rgb(92, 96, 170); low = Rgb(250, 182, 170); mtn = Rgb(150, 126, 176); snow = Rgb(255, 226, 222); snowShade = Rgb(196, 170, 206);
                    city = Rgb(132, 112, 170); cityDark = Rgb(96, 80, 138); win = Rgb(255, 214, 150); tree = Rgb(78, 70, 124); treeDark = Rgb(56, 50, 98);
                    ground = Rgb(70, 62, 112); water = Rgb(120, 110, 170); maple = Rgb(236, 110, 90); mapleDark = Rgb(170, 60, 70); lamp = Rgb(255, 214, 120); lights = 0.3f; lampOn = true; break;
                case "dia":
                    top = Rgb(84, 156, 226); low = Rgb(196, 226, 248); mtn = Rgb(110, 136, 178); snow = Rgb(250, 252, 255); snowShade = Rgb(196, 214, 236);
                    city = Rgb(150, 168, 200); cityDark = Rgb(116, 132, 170); win = Rgb(220, 236, 250); tree = Rgb(70, 130, 96); treeDark = Rgb(46, 96, 74);
                    ground = Rgb(84, 120, 96); water = Rgb(110, 170, 210); maple = Rgb(236, 96, 64); mapleDark = Rgb(176, 52, 46); lamp = Rgb(255, 230, 170); lights = 0.03f; lampOn = false; break;
                case "atardecer":
                    top = Rgb(70, 50, 120); low = Rgb(255, 150, 96); mtn = Rgb(120, 76, 120); snow = Rgb(255, 196, 160); snowShade = Rgb(176, 120, 150);
                    city = Rgb(110, 76, 130); cityDark = Rgb(80, 54, 104); win = Rgb(255, 200, 120); tree = Rgb(64, 44, 92); treeDark = Rgb(46, 32, 72);
                    ground = Rgb(58, 40, 84); water = Rgb(150, 90, 130); maple = Rgb(240, 90, 60); mapleDark = Rgb(160, 40, 50); lamp = Rgb(255, 210, 110); lights = 0.5f; lampOn = true; break;
                default:
                    top = Rgb(30, 26, 72); low = Rgb(96, 82, 160); mtn = Rgb(70, 62, 126); snow = Rgb(150, 140, 200); snowShade = Rgb(104, 96, 162);
                    city = Rgb(72, 64, 132); cityDark = Rgb(52, 46, 104); win = Rgb(255, 196, 110); tree = Rgb(46, 40, 96); treeDark = Rgb(34, 30, 76);
                    ground = Rgb(40, 34, 84); water = Rgb(60, 52, 116); maple = Rgb(220, 70, 50); mapleDark = Rgb(120, 30, 46); lamp = Rgb(255, 220, 110); lights = 0.75f; stars = true; lampOn = true; break;
            }
            int horizon = (int)(h * 0.58f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    c.Set(x, y, Dither(top, low, (float)y / horizon, x, y, 12));
            if (stars)
                for (int i = 0; i < w * h / 120; i++) { int x = rnd.Next(w), y = rnd.Next((int)(horizon * 0.7f)); int b = 160 + rnd.Next(90); c.Set(x, y, Rgb(b, b - 10, Math.Min(255, b + 30))); }

            // montaña: laderas rectas con un poco de ruido; nieve solo en la parte alta, con rayado del lado en sombra
            float peakX = w * 0.40f, peakY = h * 0.09f, baseY = horizon + h * 0.02f, half = w * 0.40f;
            for (int x = 0; x < w; x++)
            {
                float dx = Math.Abs(x - peakX) / half;
                float ridge = peakY + (baseY - peakY) * Math.Min(1f, dx * (0.9f + 0.1f * dx)) + (Fbm(x * 0.06f, 3.3f) - 0.5f) * 5f * Math.Min(1f, dx * 3);
                bool leftSide = x < peakX;
                float snowY = peakY + (baseY - peakY) * (0.36f + (Fbm(x * 0.09f, 9f) - 0.5f) * 0.22f);
                for (int y = (int)ridge; y < baseY; y++)
                {
                    float k = Math.Min(1f, (y - peakY) / (baseY - peakY));
                    Color col;
                    if (y < snowY)
                    {
                        col = leftSide ? snow : snowShade;
                        if (!leftSide && (x + y) % 3 == 0) col = snow;                      // rayado diagonal
                        if (leftSide && (x - y) % 7 == 0 && y > peakY + 6) col = snowShade;  // surcos
                    }
                    else
                    {
                        col = Dither(Mix(mtn, snowShade, leftSide ? 0.3f : 0.1f), Mix(mtn, low, 0.4f), k, x, y, 6);   // se pierde con la neblina abajo
                        if (!leftSide && (x + y) % 4 == 0) col = Mix(col, Rgb(0, 0, 0), 0.1f);
                        if (y < snowY + 4 && Hash(x, y) > 0.6f) col = snowShade;   // orilla de la nieve deshilachada
                    }
                    c.Set(x, y, col);
                }
            }

            // ciudad: torres altas y delgadas delante de la montaña
            Towers(c, glow, rnd, horizon + (int)(h * 0.10f), 0.10f, 0.34f, 8, 18, city, lights, win, 0.08f);
            int cityBase = horizon + (int)(h * 0.10f);
            c.FillRect(0, cityBase, w, h - cityBase, cityDark); glow.Clear(0, cityBase, w, h - cityBase);
            // muro del rio con barandal
            int wallY = cityBase + (int)(h * 0.03f);
            c.FillRect(0, wallY, w, 3, Mix(cityDark, low, 0.25f));
            for (int x = 0; x < w; x += 6) c.FillRect(x, wallY - 4, 1, 4, Mix(cityDark, low, 0.2f));
            c.FillRect(0, wallY - 4, w, 1, Mix(cityDark, low, 0.2f));

            // estanque abajo con reflejos
            int pondY = (int)(h * 0.80f);
            for (int y = wallY + 3; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (y < pondY) { c.Set(x, y, Dither(ground, cityDark, (float)(pondY - y) / (pondY - wallY), x, y, 4)); continue; }
                    var col = Dither(water, Mix(water, Rgb(0, 0, 0), 0.35f), (float)(y - pondY) / (h - pondY), x, y, 6);
                    if (Hash(x / 6, y) > 0.93f) col = Mix(col, low, 0.4f);   // ondas
                    c.Set(x, y, col);
                }
            glow.Clear(0, wallY, w, h - wallY);

            // arboles redondos (siluetas) a la izquierda y al centro
            RoundTree(c, rnd, (int)(w * 0.10f), pondY, 22, tree, treeDark, low);
            RoundTree(c, rnd, (int)(w * 0.30f), pondY + 2, 28, tree, treeDark, low);
            RoundTree(c, rnd, (int)(w * 0.50f), pondY, 17, tree, treeDark, low);

            // isla de roca a la derecha con el farol y un arce rojo arriba
            int rockX = (int)(w * 0.68f);
            for (int y = (int)(h * 0.70f); y < h; y++)
                for (int x = rockX; x < w; x++)
                {
                    float edge = rockX + (h - y) * 0.35f + (Fbm(x * 0.1f, y * 0.1f) - 0.5f) * 10f;
                    if (x < edge) continue;
                    c.Set(x, y, Dither(treeDark, Mix(treeDark, Rgb(0, 0, 0), 0.3f), (float)(y - h * 0.7f) / (h * 0.3f), x, y, 4));
                }
            int lx = (int)(w * 0.80f), ly = (int)(h * 0.74f);
            Lantern(c, glow, lx, ly, Mix(treeDark, low, 0.15f), lamp, lampOn);
            // juncos a la orilla del agua
            int reedY = (int)(h * 0.93f);
            for (int i = 0; i < 9; i++)
            {
                int rx = (int)(w * 0.70f) + rnd.Next((int)(w * 0.28f)), rh = 10 + rnd.Next(16);
                for (int k = 0; k < rh; k++) c.Set(rx + (k > rh - 4 ? 0 : (k / 6) % 2), reedY - k, treeDark);
                c.FillRect(rx, reedY - rh - 4, 2, 5, Mix(treeDark, maple, 0.25f));
            }
            // arce: tronco que sale de la roca y una copa de hojas rojas arriba a la derecha
            int trunkX = (int)(w * 0.93f);
            for (int y = (int)(h * 0.18f); y < (int)(h * 0.76f); y++)
            {
                int bend = (int)(Math.Sin(y * 0.05f) * 3);
                c.FillRect(trunkX + bend - 4, y, 9, 1, Mix(treeDark, Rgb(58, 36, 30), 0.55f));
            }
            for (int k = 0; k < 40; k++)   // rama hacia la izquierda
                c.FillRect(trunkX - k, (int)(h * 0.24f) + k / 3, 3, 3, Mix(treeDark, Rgb(58, 36, 30), 0.55f));
            float[][] blobs = { new[] { 0.86f, 0.04f, 0.16f }, new[] { 0.74f, 0.10f, 0.12f }, new[] { 0.97f, 0.12f, 0.12f }, new[] { 0.80f, 0.20f, 0.10f }, new[] { 0.66f, 0.04f, 0.09f }, new[] { 0.92f, 0.24f, 0.08f } };
            for (int y = 0; y < (int)(h * 0.42f); y++)
                for (int x = (int)(w * 0.52f); x < w; x++)
                {
                    float best = 9;
                    foreach (var b in blobs)
                    {
                        float bx = w * b[0], by = h * b[1], br = h * b[2] * 1.15f;
                        best = Math.Min(best, ((x - bx) * (x - bx) + (y - by) * (y - by) * 1.3f) / (br * br));
                    }
                    float n = Fbm(x * 0.045f, y * 0.045f);
                    float v = best + (0.5f - n) * 0.5f;
                    if (v > 1f) continue;
                    bool rim = v > 0.82f && Fbm(x * 0.045f, (y - 3) * 0.045f) > 0.45f;   // borde con luz
                    var col = v > 0.78f ? (rim ? Mix(maple, Rgb(255, 220, 160), 0.3f) : maple) : (n > 0.56f ? maple : (n > 0.44f ? Mix(maple, mapleDark, 0.5f) : mapleDark));
                    c.Set(x, y, col);
                    if (col == maple || rim) glow.Blend(x, y, maple, period == "dia" ? 0.04f : 0.2f);
                }
        }

        static void RoundTree(Canvas c, Random rnd, int cx, int baseY, int r, Color col, Color dark, Color light)
        {
            int trunkH = r / 3 + 2;
            c.FillRect(cx - 2, baseY - trunkH - 4, 5, trunkH + 4, dark);
            int cy = baseY - trunkH - (int)(r * 0.7f);
            // copa hecha de varios circulos (se ve esponjosa)
            var lobes = new[] { new[] { 0f, 0f, 1f }, new[] { -0.55f, 0.2f, 0.7f }, new[] { 0.55f, 0.2f, 0.7f }, new[] { -0.25f, -0.45f, 0.65f }, new[] { 0.3f, -0.4f, 0.6f } };
            for (int y = cy - r - 4; y <= cy + (int)(r * 1.2f); y++)
                for (int x = cx - (int)(r * 1.4f); x <= cx + (int)(r * 1.4f); x++)
                {
                    bool inside = false; float best = 9;
                    foreach (var l in lobes)
                    {
                        float lx = cx + l[0] * r, ly = cy + l[1] * r, lr = l[2] * r;
                        float d = ((x - lx) * (x - lx) + (y - ly) * (y - ly)) / (lr * lr);
                        if (d < 1f) { inside = true; best = Math.Min(best, d); }
                    }
                    if (!inside) continue;
                    float ny = (y - cy) / (float)r, nx = (x - cx) / (float)r;
                    var cc = Dither(col, dark, (ny + 0.1f) * 1.4f, x, y, 3);   // mas oscuro hacia abajo
                    if (nx < -0.1f && ny < -0.1f && Hash(x / 2, y / 2) > 0.55f) cc = Mix(col, light, 0.18f);   // luz de arriba a la izquierda
                    if (ny > 0.1f && Hash(x, y) > 0.8f) cc = dark;
                    c.Set(x, y, cc);
                }
        }

        // farol de piedra japones (toro): base, poste, caja de luz y techito
        static void Lantern(Canvas c, Canvas glow, int x, int baseY, Color stone, Color light, bool on)
        {
            var dark = Mix(stone, Rgb(0, 0, 0), 0.3f);
            c.FillRect(x - 6, baseY - 3, 13, 3, dark);           // base
            c.FillRect(x - 2, baseY - 22, 5, 19, stone);          // poste
            c.FillRect(x - 5, baseY - 25, 11, 3, dark);           // plato
            c.FillRect(x - 4, baseY - 35, 9, 10, on ? light : Mix(stone, light, 0.3f));   // caja de luz
            c.FillRect(x - 4, baseY - 35, 1, 10, dark); c.FillRect(x + 4, baseY - 35, 1, 10, dark);
            c.FillRect(x - 8, baseY - 38, 17, 3, dark);           // techo
            c.FillRect(x - 6, baseY - 40, 13, 2, dark);
            c.FillRect(x - 1, baseY - 43, 3, 3, dark);            // punta
            if (on)
            {
                c.FillRect(x - 2, baseY - 33, 5, 6, Mix(light, Rgb(255, 255, 255), 0.5f));
                for (int y = baseY - 60; y < baseY + 20; y++)
                    for (int xx = x - 40; xx < x + 40; xx++)
                    {
                        float d = (float)Math.Sqrt((xx - x) * (xx - x) + (y - (baseY - 30)) * (y - (baseY - 30)));
                        if (d < 40) glow.Blend(xx, y, light, 0.55f * (1f - d / 40f));
                    }
            }
        }
    }
}
