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
    // Fotograma que dura casi un numero entero de pasos (VSync o tope a 60 Hz: 16,67 ms +- el temblor del
    // reloj) cuenta como ese numero exacto. Sin esto el resto quedaba justo en el limite de un paso y el temblor
    // daba 0 pasos (imagen repetida) y luego 2 (salto): sacudida del escenario al desplazarse.
    const double SnapTol = 0.0005;
    double last = -1, acc;

    public static bool Fixed(WindowConfig cfg) => cfg.MaxFps == 60 && !cfg.VSync;

    public int Steps(bool fixedStep)
    {
        if (fixedStep) { last = -1; acc = 0; return 1; }
        double now = Raylib.GetTime();
        // Primer fotograma (o cambio desde fijo): un paso y medio paso de resto, lejos del limite entre pasos.
        if (last < 0) { last = now; acc = 1.5 * StepTime; }
        double dt = now - last; last = now;
        double k = Math.Round(dt / StepTime);
        if (k >= 1 && Math.Abs(dt - k * StepTime) < SnapTol) dt = k * StepTime;
        acc += dt;
        if (acc > (MaxCatchUp + 0.5) * StepTime) acc = (MaxCatchUp + 0.5) * StepTime;
        int n = (int)(acc / StepTime);
        acc -= n * StepTime;
        return n;
    }
}
