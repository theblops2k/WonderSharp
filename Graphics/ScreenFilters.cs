using System.Numerics;
using Raylib_cs;

namespace WonderBoy.Graphics;

// Filtros de pantalla (menu Tab > GRAPHICS > Filters; Config.cfg: Filters=...). Se aplican solo al dibujar la
// imagen del juego en GameWindow.Draw (ORIGINAL, WIDESCREEN y ventana); la emulacion no cambia.
// Trabajan sobre la rejilla logica 256xN (la textura de TitleRenderer / WideView tiene pixel logico 2x1).
// Grupos (dentro de cada grupo solo uno activo; al encender uno se apagan los demas del grupo):
//   Escalado final:  Bilinear, Sharp Bilinear (bilinear solo en el borde de cada pixel), FSR (AMD FSR 1.0:
//                    EASU + RCAS, dos pasadas)
//   Pixel art:       XBR (xBR nivel 1, a N x con bordes suavizados), Scale3x (AdvMAME3x, 3x exacto); su salida
//                    pasa luego por el escalado final (Sharp Bilinear si no hay ninguno elegido)
//   Efectos:         Vibrance (satura mas los colores apagados), Scanlines (lineas de barrido, 224 lineas)
[Flags]
public enum ScreenFilter
{
    None = 0,
    Bilinear = 1,
    SharpBilinear = 2,
    Xbr = 4,
    Scale3x = 8,
    Fsr = 16,
    Vibrance = 32,
    Scanlines = 64,
}

public static class ScreenFilters
{
    public const ScreenFilter Scalers = ScreenFilter.Bilinear | ScreenFilter.SharpBilinear | ScreenFilter.Fsr;
    public const ScreenFilter PixelArt = ScreenFilter.Xbr | ScreenFilter.Scale3x;

    // Orden del menu y de Config.cfg.
    public static readonly ScreenFilter[] All =
    {
        ScreenFilter.Bilinear, ScreenFilter.SharpBilinear, ScreenFilter.Xbr, ScreenFilter.Scale3x,
        ScreenFilter.Fsr, ScreenFilter.Vibrance, ScreenFilter.Scanlines,
    };

    public static string Label(ScreenFilter f) => f switch
    {
        ScreenFilter.Bilinear => "Bilinear",
        ScreenFilter.SharpBilinear => "Sharp Bilinear",
        ScreenFilter.Xbr => "XBR",
        ScreenFilter.Scale3x => "Scale3x",
        ScreenFilter.Fsr => "FSR",
        ScreenFilter.Vibrance => "Vibrance",
        ScreenFilter.Scanlines => "Scanlines",
        _ => "",
    };

    static string Key(ScreenFilter f) => Label(f).Replace(" ", "").ToUpperInvariant();   // BILINEAR, SHARPBILINEAR...

    static ScreenFilter Group(ScreenFilter f) => (Scalers & f) != 0 ? Scalers : (PixelArt & f) != 0 ? PixelArt : f;

    // Enciende / apaga f; al encender apaga los otros de su grupo.
    public static ScreenFilter Set(ScreenFilter set, ScreenFilter f, bool on) => on ? (set & ~Group(f)) | f : set & ~f;

    // "NONE" o lista separada por comas: "XBR,FSR,SCANLINES".
    public static string ToText(ScreenFilter set)
    {
        var names = All.Where(f => (set & f) != 0).Select(Key).ToList();
        return names.Count == 0 ? "NONE" : string.Join(",", names);
    }

    // Nombres no reconocidos se ignoran; si hay dos del mismo grupo gana el primero de la lista.
    public static ScreenFilter Parse(string text)
    {
        var set = ScreenFilter.None;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = All.FirstOrDefault(x => Key(x) == part.ToUpperInvariant());
            if (f != ScreenFilter.None && (set & Group(f)) == 0) set |= f;
        }
        return set;
    }
}

// Dibujo con filtros: hasta 3 pasadas de shader (pixel art -> escalado final -> efectos en la ultima pasada).
// Los shaders y las RenderTexture se crean al primer uso (despues de InitWindow).
public sealed class FilterRenderer
{
    const float VibranceAmount = 0.45f, ScanlineAmount = 0.45f, FsrSharpness = 0.2f;   // RCAS: 0 = maximo

    sealed class Fx
    {
        public Shader S;
        public int Src, Out, K, Vib, Scan, Lines, Sharp;
        public Fx(string fs)
        {
            S = Raylib.LoadShaderFromMemory(Vs, fs);
            Src = Raylib.GetShaderLocation(S, "srcSize"); Out = Raylib.GetShaderLocation(S, "outSize");
            K = Raylib.GetShaderLocation(S, "k"); Vib = Raylib.GetShaderLocation(S, "vibrance");
            Scan = Raylib.GetShaderLocation(S, "scanlines"); Lines = Raylib.GetShaderLocation(S, "lines");
            Sharp = Raylib.GetShaderLocation(S, "sharpness");
        }
    }

    Fx? nearest, bilinear, sharp, xbr, scale3x, easu, rcas;
    RenderTexture2D pre, fsr;

    static void V2(Shader s, int loc, float x, float y) { if (loc >= 0) Raylib.SetShaderValue(s, loc, new Vector2(x, y), ShaderUniformDataType.Vec2); }
    static void F1(Shader s, int loc, float v) { if (loc >= 0) Raylib.SetShaderValue(s, loc, v, ShaderUniformDataType.Float); }

    static RenderTexture2D Target(ref RenderTexture2D rt, int w, int h)
    {
        if (rt.Id != 0 && rt.Texture.Width == w && rt.Texture.Height == h) return rt;
        if (rt.Id != 0) Raylib.UnloadRenderTexture(rt);
        rt = Raylib.LoadRenderTexture(w, h);
        return rt;
    }

    // Una pasada a una RenderTexture de w x h.
    static void Pass(RenderTexture2D rt, Fx fx, Texture2D src, Rectangle srcRect)
    {
        Raylib.BeginTextureMode(rt);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(fx.S);
        Raylib.DrawTexturePro(src, srcRect, new Rectangle(0, 0, rt.Texture.Width, rt.Texture.Height), Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    // tex: imagen del juego (pixel logico 2x1); dst: rectangulo en pantalla (el mismo que sin filtros).
    public void Draw(Texture2D tex, Rectangle dst, ScreenFilter f)
    {
        int lw = Math.Max(1, tex.Width / 2), lh = tex.Height;
        int ox = (int)MathF.Round(dst.X), oy = (int)MathF.Round(dst.Y);
        int ow = Math.Max(1, (int)MathF.Round(dst.Width)), oh = Math.Max(1, (int)MathF.Round(dst.Height));

        Texture2D src = tex;
        var srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
        float sw = lw, sh = lh;

        // 1) Pixel art: XBR (N x segun la altura de salida, 2..6) o Scale3x (3x).
        bool pixelArt = (f & ScreenFilters.PixelArt) != 0;
        if (pixelArt)
        {
            bool s3 = (f & ScreenFilter.Scale3x) != 0;
            int k = s3 ? 3 : Math.Clamp((int)MathF.Round(oh / (float)lh), 2, 6);
            var fx = s3 ? (scale3x ??= new Fx(Scale3xFs)) : (xbr ??= new Fx(XbrFs));
            V2(fx.S, fx.Src, lw, lh);
            F1(fx.S, fx.K, k);
            var rt = Target(ref pre, lw * k, lh * k);
            Pass(rt, fx, src, srcRect);
            src = rt.Texture; srcRect = new Rectangle(0, 0, rt.Texture.Width, -rt.Texture.Height);   // RenderTexture: Y invertida
            sw = rt.Texture.Width; sh = rt.Texture.Height;
        }

        // 2) Escalado final (+ efectos en la misma pasada).
        Fx last;
        if ((f & ScreenFilter.Fsr) != 0)
        {
            var e = easu ??= new Fx(EasuFs);
            V2(e.S, e.Src, sw, sh);
            var rt = Target(ref fsr, ow, oh);
            Pass(rt, e, src, srcRect);
            src = rt.Texture; srcRect = new Rectangle(0, 0, ow, -oh);
            sw = ow; sh = oh;
            last = rcas ??= new Fx(RcasFs);
            F1(last.S, last.Sharp, FsrSharpness);
        }
        else if ((f & ScreenFilter.Bilinear) != 0) last = bilinear ??= new Fx(BilinearFs);
        else if ((f & ScreenFilter.SharpBilinear) != 0 || pixelArt) last = sharp ??= new Fx(SharpFs);
        else last = nearest ??= new Fx(NearestFs);

        V2(last.S, last.Src, sw, sh);
        V2(last.S, last.Out, ow, oh);
        F1(last.S, last.Vib, (f & ScreenFilter.Vibrance) != 0 ? VibranceAmount : 0f);
        F1(last.S, last.Scan, (f & ScreenFilter.Scanlines) != 0 ? ScanlineAmount : 0f);
        F1(last.S, last.Lines, lh);
        Raylib.BeginShaderMode(last.S);
        Raylib.DrawTexturePro(src, srcRect, new Rectangle(ox, oy, ow, oh), Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
    }

    // ---- shaders (GLSL 330, raylib) ---------------------------------------------------------

    const string Vs = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec4 vertexColor;
        out vec2 fragTexCoord;
        out vec4 fragColor;
        uniform mat4 mvp;
        void main() { fragTexCoord = vertexTexCoord; fragColor = vertexColor; gl_Position = mvp * vec4(vertexPosition, 1.0); }
        """;

    // Cabecera comun: P(ip) = pixel logico ip (recortado al borde); con la textura 2x1 lee la columna 2*x.
    const string Head = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        out vec4 finalColor;
        uniform sampler2D texture0;
        uniform vec2 srcSize;
        vec3 P(vec2 ip)
        {
            ivec2 ts = textureSize(texture0, 0);
            ivec2 n = ivec2(srcSize + 0.5);
            ivec2 q = clamp(ivec2(floor(ip)), ivec2(0), n - 1);
            return texelFetch(texture0, ivec2(q.x * max(1, ts.x / n.x), q.y), 0).rgb;
        }
        vec3 Bil(vec2 t)
        {
            vec2 q = t - 0.5, i = floor(q), f = q - i;
            return mix(mix(P(i), P(i + vec2(1, 0)), f.x), mix(P(i + vec2(0, 1)), P(i + vec2(1, 1)), f.x), f.y);
        }

        """;

    // Efectos de la ultima pasada: Vibrance y Scanlines (fragTexCoord.y 0..1 = las 224 lineas).
    const string Post = """
        uniform float vibrance;
        uniform float scanlines;
        uniform float lines;
        vec3 Post(vec3 c)
        {
            if (vibrance > 0.0)
            {
                float luma = dot(c, vec3(0.299, 0.587, 0.114));
                float sat = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
                c = clamp(mix(vec3(luma), c, 1.0 + vibrance * (1.0 - sat)), 0.0, 1.0);
            }
            if (scanlines > 0.0)
            {
                float t = fract(fragTexCoord.y * lines);
                float b = 0.5 + 0.5 * cos(6.2831853 * (t - 0.25));
                c = clamp(c * mix(1.0 - scanlines, 1.0, b) * (1.0 + 0.4 * scanlines), 0.0, 1.0);
            }
            return c;
        }

        """;

    const string NearestFs = Head + Post + """
        void main() { finalColor = vec4(Post(texture(texture0, fragTexCoord).rgb), 1.0); }
        """;

    const string BilinearFs = Head + Post + """
        void main() { finalColor = vec4(Post(Bil(fragTexCoord * srcSize)), 1.0); }
        """;

    // Sharp Bilinear: pixel entero en el centro, mezcla bilinear solo en una franja de ~1 pixel de salida.
    const string SharpFs = Head + Post + """
        uniform vec2 outSize;
        void main()
        {
            vec2 t = fragTexCoord * srcSize;
            vec2 pre = max(floor(outSize / srcSize), vec2(1.0));
            vec2 range = 0.5 - 0.5 / pre;
            vec2 cd = fract(t) - 0.5;
            vec2 f = (cd - clamp(cd, -range, range)) * pre + 0.5;
            finalColor = vec4(Post(Bil(floor(t) + f)), 1.0);
        }
        """;

    // Scale3x (AdvMAME3x): cada pixel E -> 3x3 segun sus vecinos iguales (A B C / D E F / G H I).
    const string Scale3xFs = Head + """
        bool Eq(vec3 a, vec3 b) { return all(lessThan(abs(a - b), vec3(1.0 / 512.0))); }
        void main()
        {
            vec2 p = fragTexCoord * srcSize, ip = floor(p);
            ivec2 s = ivec2(min(floor(fract(p) * 3.0), vec2(2.0)));
            vec3 A = P(ip + vec2(-1, -1)), B = P(ip + vec2(0, -1)), C = P(ip + vec2(1, -1));
            vec3 D = P(ip + vec2(-1, 0)),  E = P(ip),               F = P(ip + vec2(1, 0));
            vec3 G = P(ip + vec2(-1, 1)),  H = P(ip + vec2(0, 1)),  I = P(ip + vec2(1, 1));
            vec3 o = E;
            if (!Eq(B, H) && !Eq(D, F))
            {
                int n = s.y * 3 + s.x;
                if (n == 0) o = Eq(D, B) ? D : E;
                else if (n == 1) o = (Eq(D, B) && !Eq(E, C)) || (Eq(B, F) && !Eq(E, A)) ? B : E;
                else if (n == 2) o = Eq(B, F) ? F : E;
                else if (n == 3) o = (Eq(D, B) && !Eq(E, G)) || (Eq(D, H) && !Eq(E, A)) ? D : E;
                else if (n == 5) o = (Eq(B, F) && !Eq(E, I)) || (Eq(H, F) && !Eq(E, C)) ? F : E;
                else if (n == 6) o = Eq(D, H) ? D : E;
                else if (n == 7) o = (Eq(D, H) && !Eq(E, I)) || (Eq(H, F) && !Eq(E, G)) ? H : E;
                else if (n == 8) o = Eq(H, F) ? F : E;
            }
            finalColor = vec4(o, 1.0);
        }
        """;

    // xBR nivel 1 (Hyllian): por cada esquina del pixel compara la variacion en las dos diagonales (distancia
    // YUV ponderada) y, si hay borde, rellena el triangulo de la esquina (x + y > 1.5) con el vecino mas parecido.
    // El vecindario se refleja para que la esquina tratada sea siempre la de abajo a la derecha.
    const string XbrFs = Head + """
        uniform float k;
        float Df(vec3 a, vec3 b)
        {
            vec3 d = a - b;
            return 48.0 * abs(dot(d, vec3(0.299, 0.587, 0.114))) + 7.0 * abs(dot(d, vec3(-0.169, -0.331, 0.5)))
                 + 6.0 * abs(dot(d, vec3(0.5, -0.419, -0.081)));
        }
        void main()
        {
            vec2 p = fragTexCoord * srcSize, ip = floor(p), fp = fract(p);
            vec2 dir = vec2(fp.x < 0.5 ? -1.0 : 1.0, fp.y < 0.5 ? -1.0 : 1.0);
            vec2 f = vec2(dir.x > 0.0 ? fp.x : 1.0 - fp.x, dir.y > 0.0 ? fp.y : 1.0 - fp.y);
            vec3 E = P(ip), B = P(ip + vec2(0, -1) * dir), C = P(ip + vec2(1, -1) * dir), D = P(ip + vec2(-1, 0) * dir);
            vec3 F = P(ip + vec2(1, 0) * dir), G = P(ip + vec2(-1, 1) * dir), H = P(ip + vec2(0, 1) * dir);
            vec3 I = P(ip + vec2(1, 1) * dir), F4 = P(ip + vec2(2, 0) * dir), I4 = P(ip + vec2(2, 1) * dir);
            vec3 H5 = P(ip + vec2(0, 2) * dir), I5 = P(ip + vec2(1, 2) * dir);
            float wd1 = Df(E, C) + Df(E, G) + Df(I, H5) + Df(I, F4) + 4.0 * Df(H, F);
            float wd2 = Df(H, D) + Df(H, I5) + Df(F, I4) + Df(F, B) + 4.0 * Df(E, I);
            bool edge = wd1 < wd2 && Df(E, F) > 0.001 && Df(E, H) > 0.001;
            vec3 px = Df(E, F) <= Df(E, H) ? F : H;
            float a = edge ? clamp((f.x + f.y - 1.5) * k * 0.5 + 0.5, 0.0, 1.0) : 0.0;
            finalColor = vec4(mix(E, px, a), 1.0);
        }
        """;

    // FSR 1.0 EASU (AMD): 12 muestras alrededor de la posicion, direccion y longitud del borde a partir de la
    // luma de los 4 pixeles centrales, nucleo tipo Lanczos estirado a lo largo del borde, sin halos (min/max).
    const string EasuFs = Head + """
        float L(vec3 c) { return c.g + 0.5 * (c.r + c.b); }
        void SetF(inout vec2 dir, inout float len, float w, float lA, float lB, float lC, float lD, float lE)
        {
            float dx = lD - lB, mx = max(abs(lD - lC), abs(lC - lB));
            float lx = mx > 0.0 ? clamp(abs(dx) / mx, 0.0, 1.0) : 0.0;
            dir.x += dx * w; len += lx * lx * w;
            float dy = lE - lA, my = max(abs(lE - lC), abs(lC - lA));
            float ly = my > 0.0 ? clamp(abs(dy) / my, 0.0, 1.0) : 0.0;
            dir.y += dy * w; len += ly * ly * w;
        }
        void Tap(inout vec3 aC, inout float aW, vec2 off, vec2 dir, vec2 len, float lob, float clp, vec3 c)
        {
            vec2 v = vec2(dot(off, dir), dot(off, vec2(-dir.y, dir.x))) * len;
            float d2 = min(dot(v, v), clp);
            float wB = 0.4 * d2 - 1.0, wA = lob * d2 - 1.0;
            wB *= wB; wA *= wA;
            wB = 1.5625 * wB - 0.5625;
            float w = wB * wA;
            aW += w; aC += c * w;
        }
        void main()
        {
            vec2 pp = fragTexCoord * srcSize - 0.5, fp = floor(pp);
            pp -= fp;
            vec3 b = P(fp + vec2(0, -1)), c = P(fp + vec2(1, -1));
            vec3 e = P(fp + vec2(-1, 0)), f = P(fp), g = P(fp + vec2(1, 0)), h = P(fp + vec2(2, 0));
            vec3 i = P(fp + vec2(-1, 1)), j = P(fp + vec2(0, 1)), k = P(fp + vec2(1, 1)), l = P(fp + vec2(2, 1));
            vec3 n = P(fp + vec2(0, 2)), o = P(fp + vec2(1, 2));
            float bL = L(b), cL = L(c), eL = L(e), fL = L(f), gL = L(g), hL = L(h);
            float iL = L(i), jL = L(j), kL = L(k), lL = L(l), nL = L(n), oL = L(o);
            vec2 dir = vec2(0.0);
            float len = 0.0;
            SetF(dir, len, (1.0 - pp.x) * (1.0 - pp.y), bL, eL, fL, gL, jL);
            SetF(dir, len, pp.x * (1.0 - pp.y), cL, fL, gL, hL, kL);
            SetF(dir, len, (1.0 - pp.x) * pp.y, fL, iL, jL, kL, nL);
            SetF(dir, len, pp.x * pp.y, gL, jL, kL, lL, oL);
            float dirR = dot(dir, dir);
            bool zro = dirR < 1.0 / 32768.0;
            dirR = zro ? 1.0 : inversesqrt(dirR);
            dir.x = zro ? 1.0 : dir.x;
            dir *= dirR;
            len = len * 0.5; len *= len;
            float stretch = dot(dir, dir) / max(abs(dir.x), abs(dir.y));
            vec2 len2 = vec2(1.0 + (stretch - 1.0) * len, 1.0 - 0.5 * len);
            float lob = 0.5 + ((1.0 / 4.0 - 0.04) - 0.5) * len;
            float clp = 1.0 / lob;
            vec3 aC = vec3(0.0);
            float aW = 0.0;
            Tap(aC, aW, vec2(0, -1) - pp, dir, len2, lob, clp, b);
            Tap(aC, aW, vec2(1, -1) - pp, dir, len2, lob, clp, c);
            Tap(aC, aW, vec2(-1, 1) - pp, dir, len2, lob, clp, i);
            Tap(aC, aW, vec2(0, 1) - pp, dir, len2, lob, clp, j);
            Tap(aC, aW, vec2(0, 0) - pp, dir, len2, lob, clp, f);
            Tap(aC, aW, vec2(-1, 0) - pp, dir, len2, lob, clp, e);
            Tap(aC, aW, vec2(1, 1) - pp, dir, len2, lob, clp, k);
            Tap(aC, aW, vec2(2, 1) - pp, dir, len2, lob, clp, l);
            Tap(aC, aW, vec2(2, 0) - pp, dir, len2, lob, clp, h);
            Tap(aC, aW, vec2(1, 0) - pp, dir, len2, lob, clp, g);
            Tap(aC, aW, vec2(1, 2) - pp, dir, len2, lob, clp, o);
            Tap(aC, aW, vec2(0, 2) - pp, dir, len2, lob, clp, n);
            vec3 mn = min(min(f, g), min(j, k)), mx = max(max(f, g), max(j, k));
            vec3 r = aW > 0.0 ? aC / aW : f;
            finalColor = vec4(clamp(r, mn, mx), 1.0);
        }
        """;

    // FSR 1.0 RCAS (AMD): nitidez adaptativa en cruz sobre la salida de EASU, limitada para no crear halos.
    const string RcasFs = Head + Post + """
        uniform float sharpness;
        void main()
        {
            vec2 ip = floor(fragTexCoord * srcSize);
            vec3 b = P(ip + vec2(0, -1)), d = P(ip + vec2(-1, 0)), e = P(ip), f = P(ip + vec2(1, 0)), h = P(ip + vec2(0, 1));
            vec3 mn4 = min(min(b, d), min(f, h)), mx4 = max(max(b, d), max(f, h));
            vec3 hitMin = min(mn4, e) / max(4.0 * mx4, vec3(1e-5));
            vec3 hitMax = (1.0 - max(mx4, e)) / min(4.0 * mn4 - 4.0, vec3(-1e-5));
            vec3 lobeRGB = max(-hitMin, hitMax);
            float lobe = max(-0.1875, min(max(lobeRGB.r, max(lobeRGB.g, lobeRGB.b)), 0.0)) * exp2(-sharpness);
            vec3 c = (lobe * (b + d + f + h) + e) / (4.0 * lobe + 1.0);
            finalColor = vec4(Post(clamp(c, 0.0, 1.0)), 1.0);
        }
        """;
}
