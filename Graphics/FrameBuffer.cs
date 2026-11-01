using Raylib_cs;

namespace WonderBoy.Graphics;

// Imagen del fotograma en memoria (RGBA8, fila a fila, Width x 224): TitleRenderer.Render y WideView.Render
// escriben aqui el color de cada pixel y Upload lo sube a una unica textura (UpdateTexture). El array y la
// textura se reservan una vez; solo se rehacen si cambia el ancho (ORIGINAL / WIDESCREEN). Filtro Point.
public sealed class FrameBuffer
{
    public int Width { get; private set; } = TitleRenderer.Width;
    public int Height => TitleRenderer.Height;
    public Color[] Pixels { get; private set; } = Array.Empty<Color>();
    Texture2D tex;

    // Ancho del fotograma; el array solo crece (al volver a un ancho menor se usa su parte inicial).
    public void Resize(int width)
    {
        Width = width;
        int n = width * Height;
        if (Pixels.Length < n) Pixels = new Color[n];
    }

    // Sube el fotograma a la textura (la crea, o la rehace si cambio el ancho) y la devuelve.
    public unsafe Texture2D Upload()
    {
        if (tex.Id == 0 || tex.Width != Width)
        {
            if (tex.Id != 0) Raylib.UnloadTexture(tex);
            var img = Raylib.GenImageColor(Width, Height, Color.Black);
            tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.SetTextureFilter(tex, TextureFilter.Point);
        }
        fixed (Color* p = Pixels) Raylib.UpdateTexture(tex, p);
        return tex;
    }

    // Copia del fotograma como Image RGBA8 (exportar / comparar); quien la recibe la libera (UnloadImage).
    public unsafe Image ToImage()
    {
        var img = Raylib.GenImageColor(Width, Height, Color.Black);
        long bytes = (long)Width * Height * sizeof(Color);
        fixed (Color* p = Pixels) Buffer.MemoryCopy(p, img.Data, bytes, bytes);
        return img;
    }

    public void Unload()
    {
        if (tex.Id != 0) Raylib.UnloadTexture(tex);
        tex = default;
    }
}
