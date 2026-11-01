namespace WonderBoy.Audio;

// Convierte los bytes que SoundProgram escribe en $A000/$C000 en notas (tono y volumen) por voz.
// Formato de los bytes (el que construye $070D): 1rrr dddd = registro r y 4 bits bajos,
// 0-dddddd = 6 bits altos del periodo del ultimo registro de tono. r: 0/2/4 periodo de tono 0-2,
// 1/3/5/7 atenuacion de tono 0-2 y ruido (0 = maximo, 15 = silencio), 6 control de ruido
// (bit 2: 1 blanco / 0 periodico; bits 0-1: velocidad reloj/512, /1024, /2048 o la del tono 2).
// Frecuencia de tono = reloj / (32 * periodo); sn1 a 2 MHz, sn2 a 4 MHz (docs/wboy2u-hardware.md).
public sealed class ChipNotes
{
    public static readonly double[] Clock = { 2_000_000.0, 4_000_000.0 };   // SoundProgram.Sn1, Sn2

    public readonly int[,] Period = new int[2, 3];
    public readonly int[,] Atten = new int[2, 4];
    public readonly int[] NoiseCtrl = new int[2];
    readonly int[] latch = new int[2];

    public ChipNotes()
    {
        for (int c = 0; c < 2; c++) for (int v = 0; v < 4; v++) Atten[c, v] = 15;
    }

    public void Write(int chip, byte b)
    {
        if ((b & 0x80) != 0)
        {
            int r = (b >> 4) & 7;
            latch[chip] = r;
            if (r == 6) NoiseCtrl[chip] = b & 7;
            else if ((r & 1) != 0) Atten[chip, r >> 1] = b & 0x0F;
            else Period[chip, r >> 1] = (Period[chip, r >> 1] & 0x3F0) | (b & 0x0F);
        }
        else
        {
            int r = latch[chip];
            if (r == 6) NoiseCtrl[chip] = b & 7;
            else if ((r & 1) != 0) Atten[chip, r >> 1] = b & 0x0F;
            else Period[chip, r >> 1] = ((b & 0x3F) << 4) | (Period[chip, r >> 1] & 0x0F);
        }
    }

    public double ToneHz(int chip, int v)
    {
        int p = Period[chip, v];
        return Clock[chip] / (32.0 * (p == 0 ? 1024 : p));
    }

    // Pasos por segundo del generador de ruido.
    public double NoiseHz(int chip)
    {
        int r = NoiseCtrl[chip] & 3;
        return r == 3 ? ToneHz(chip, 2) : Clock[chip] / (512 << r);
    }

    public bool NoiseWhite(int chip) => (NoiseCtrl[chip] & 4) != 0;

    // Volumen relativo de 2 dB por paso; 15 = silencio.
    public static double Level(int atten) => atten >= 15 ? 0.0 : Math.Pow(10.0, -0.1 * atten);

    // Voces del generador: chip * 4 + (0-2 tono, 3 ruido).
    public void ApplyTo(ToneGenerator g)
    {
        for (int c = 0; c < 2; c++)
        {
            for (int v = 0; v < 3; v++) g.SetNote(c * 4 + v, Wave.Square, ToneHz(c, v), Level(Atten[c, v]));
            g.SetNote(c * 4 + 3, NoiseWhite(c) ? Wave.WhiteNoise : Wave.PeriodicNoise, NoiseHz(c), Level(Atten[c, 3]));
        }
    }

    static readonly string[] Names = { "Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si" };

    // Nombre de la nota mas cercana (La4 = 440 Hz) y desviacion en centesimas.
    public static string NoteName(double hz, bool withCents = true)
    {
        double m = 69 + 12 * Math.Log2(hz / 440.0);
        int n = (int)Math.Round(m);
        int cents = (int)Math.Round((m - n) * 100);
        string s = $"{Names[((n % 12) + 12) % 12]}{n / 12 - 1}";
        return cents == 0 || !withCents ? s : $"{s}{(cents > 0 ? "+" : "")}{cents}";
    }
}
