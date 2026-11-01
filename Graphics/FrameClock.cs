using Raylib_cs;

namespace WonderBoy.Graphics;

// Reloj del bucle de ventana (--play y --attract): cuantos pasos de logica tocan en este fotograma (60 por
// segundo, la velocidad del juego no cambia). Fijo (MaxFPS = 60 sin VSync): un paso por fotograma, como
// siempre (SetTargetFPS(60) marca el ritmo). Si no (MaxFPS != 60 o VSync, que ata los fotogramas al monitor):
// pasos por tiempo real; en los fotogramas sin paso nuevo se vuelve a dibujar el ultimo estado (sin interpolar).
public sealed class FrameClock
{
    public const double StepTime = 1.0 / 60;
    const int MaxCatchUp = 4;                    // equipo lento: como mucho 4 pasos por fotograma
    double last = -1, acc;

    public static bool Fixed(WindowConfig cfg) => cfg.MaxFps == 60 && !cfg.VSync;

    public int Steps(bool fixedStep)
    {
        if (fixedStep) { last = -1; acc = 0; return 1; }
        double now = Raylib.GetTime();
        if (last < 0) { last = now; acc = StepTime; }        // primer fotograma (o cambio desde fijo): un paso
        acc += now - last; last = now;
        if (acc > MaxCatchUp * StepTime) acc = MaxCatchUp * StepTime;
        int n = (int)(acc / StepTime);
        acc -= n * StepTime;
        return n;
    }
}
