namespace WonderBoy.Audio;

public enum Wave { Square, WhiteNoise, PeriodicNoise }

// Generador de onda propio: voces de onda cuadrada (con suavizado PolyBLEP contra el aliasing) y de
// ruido (registro de desplazamiento de 15 bits: blanco con realimentacion bits 0^1, periodico con
// un solo bit que rota). Cada voz recibe una nota (forma, frecuencia, nivel 0..1) y una ganancia fija.
public sealed class ToneGenerator
{
    public const int SampleRate = 44100;

    struct Voice
    {
        public Wave Wave;
        public double Hz, Level, Gain, Phase;
        public int Lfsr;
    }

    readonly Voice[] voices;
    readonly double scale;

    public ToneGenerator(params double[] gains)
    {
        voices = new Voice[gains.Length];
        double sum = 0;
        for (int i = 0; i < gains.Length; i++) { voices[i].Gain = gains[i]; voices[i].Lfsr = 0x4000; sum += gains[i]; }
        scale = sum > 0 ? 2.0 / sum : 0;   // 2/sum: margen para varias voces a la vez (se recorta al final)
    }

    public void SetNote(int i, Wave wave, double hz, double level)
    {
        ref var v = ref voices[i];
        if (v.Wave != wave) v.Lfsr = 0x4000;   // ruido nuevo: reinicia el registro
        v.Wave = wave; v.Hz = hz; v.Level = level;
    }

    public void Render(Span<short> dst)
    {
        for (int n = 0; n < dst.Length; n++)
        {
            double mix = 0;
            for (int i = 0; i < voices.Length; i++)
            {
                ref var v = ref voices[i];
                if (v.Level <= 0) continue;
                double dt = v.Hz / SampleRate;
                double s;
                if (v.Wave == Wave.Square)
                {
                    if (dt <= 0 || dt >= 0.5) continue;   // fuera del rango audible a 44100 Hz
                    double t = v.Phase;
                    s = t < 0.5 ? 1.0 : -1.0;
                    s += Blep(t, dt);
                    s -= Blep(t < 0.5 ? t + 0.5 : t - 0.5, dt);
                    v.Phase = t + dt;
                    if (v.Phase >= 1.0) v.Phase -= 1.0;
                }
                else
                {
                    s = (v.Lfsr & 1) != 0 ? 1.0 : -1.0;
                    v.Phase += Math.Min(dt, 64.0);
                    while (v.Phase >= 1.0)
                    {
                        v.Phase -= 1.0;
                        int fb = v.Wave == Wave.WhiteNoise ? (v.Lfsr ^ (v.Lfsr >> 1)) & 1 : v.Lfsr & 1;
                        v.Lfsr = (v.Lfsr >> 1) | (fb << 14);
                        if (v.Lfsr == 0) v.Lfsr = 0x4000;
                    }
                }
                mix += s * v.Level * v.Gain;
            }
            double o = mix * scale;
            dst[n] = (short)(Math.Clamp(o, -1.0, 1.0) * 32000);
        }
    }

    static double Blep(double t, double dt)
    {
        if (t < dt) { double x = t / dt; return x + x - x * x - 1.0; }
        if (t > 1.0 - dt) { double x = (t - 1.0) / dt; return x * x + x + x + 1.0; }
        return 0.0;
    }
}
