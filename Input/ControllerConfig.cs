using Raylib_cs;

namespace WonderBoy.Input;

// Controllers.cfg (carpeta del ejecutable). Lineas "Clave=Valor", sin distinguir mayusculas:
//   Keyboard=T/F         usar el teclado para mover, saltar y disparar (defecto T)
//   Joystick=T/F         usar los joysticks (defecto T): cruceta o palanca izquierda = izquierda / derecha
//   KeyLeft=Left         teclas (nombres de Raylib_cs.KeyboardKey): izquierda, derecha, salto y disparo
//   KeyRight=Right
//   KeyJump=LeftAlt      salto  = BUTTON2 ($C00C bit2; $00A4 -> $C084 bit5)
//   KeyFire=LeftControl  disparo = BUTTON1 ($C00C bit1; $00A4 -> $C084 bit4, hacha $2B4F)
//   JoyJump=RightFaceDown  botones (nombres de Raylib_cs.GamepadButton) de salto y disparo del joystick
//   JoyFire=RightFaceLeft
// Moneda (5) y START (1) siguen en el teclado. Si el archivo no existe se crea con estos valores;
// valores no reconocidos -> defecto. El menu de opciones (Tab) cambia los valores y llama a Save.
public enum JoyBrand { Sony, Microsoft, Compatible }

// Joystick detectado: Index = numero de raylib (0..3), Number = orden de deteccion (#1, #2...).
public readonly record struct JoyInfo(int Index, int Number, string Name, JoyBrand Brand)
{
    public string Label => $"{ControllerConfig.BrandText(Brand)} Joy #{Number}";
}

public sealed class ControllerConfig
{
    public const string FileName = "Controllers.cfg";
    public const int MaxPads = 4;                        // raylib (MAX_GAMEPADS)
    const float AxisDead = 0.5f;                         // palanca: mas de la mitad = direccion pulsada

    public static readonly KeyboardKey[] DefaultKeys = { KeyboardKey.Left, KeyboardKey.Right, KeyboardKey.LeftAlt, KeyboardKey.LeftControl };
    public static readonly GamepadButton[] DefaultButtons = { GamepadButton.RightFaceDown, GamepadButton.RightFaceLeft };
    static readonly string[] KeyNames = { "KeyLeft", "KeyRight", "KeyJump", "KeyFire" };
    static readonly string[] ButtonNames = { "JoyJump", "JoyFire" };

    public bool Keyboard { get; set; } = true;
    public bool Joystick { get; set; } = true;
    // Teclas: 0 izquierda, 1 derecha, 2 salto, 3 disparo. Botones: 0 salto, 1 disparo.
    public readonly KeyboardKey[] Keys = (KeyboardKey[])DefaultKeys.Clone();
    public readonly GamepadButton[] Buttons = (GamepadButton[])DefaultButtons.Clone();
    public string Path { get; private set; } = "";

    public static ControllerConfig Load()
    {
        var c = new ControllerConfig { Path = System.IO.Path.Combine(AppContext.BaseDirectory, FileName) };
        if (!File.Exists(c.Path)) { c.Save(); return c; }
        try
        {
            foreach (var raw in File.ReadAllLines(c.Path))
            {
                int eq = raw.IndexOf('=');
                if (eq < 0) continue;
                string key = raw[..eq].Trim(), val = raw[(eq + 1)..].Trim();
                string ku = key.ToUpperInvariant(), vu = val.ToUpperInvariant();
                if (ku == "KEYBOARD" && (vu == "T" || vu == "F")) c.Keyboard = vu == "T";
                else if (ku == "JOYSTICK" && (vu == "T" || vu == "F")) c.Joystick = vu == "T";
                for (int i = 0; i < KeyNames.Length; i++)
                    if (ku == KeyNames[i].ToUpperInvariant() && Enum.TryParse(val, true, out KeyboardKey k)
                        && Enum.IsDefined(k) && Assignable(k)) c.Keys[i] = k;
                for (int i = 0; i < ButtonNames.Length; i++)
                    if (ku == ButtonNames[i].ToUpperInvariant() && Enum.TryParse(val, true, out GamepadButton b)
                        && Enum.IsDefined(b) && AssignableButton(b)) c.Buttons[i] = b;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (!c.Keyboard && !c.Joystick) c.Keyboard = true;   // sin ninguno no se podria jugar
        return c;
    }

    public void Save()
    {
        var lines = new List<string> { "Keyboard=" + (Keyboard ? "T" : "F"), "Joystick=" + (Joystick ? "T" : "F") };
        for (int i = 0; i < KeyNames.Length; i++) lines.Add($"{KeyNames[i]}={Keys[i]}");
        for (int i = 0; i < ButtonNames.Length; i++) lines.Add($"{ButtonNames[i]}={Buttons[i]}");
        try { File.WriteAllLines(Path, lines); }
        catch (IOException) { }                          // bloqueado / solo lectura: vale para esta sesion
        catch (UnauthorizedAccessException) { }
    }

    // Teclas reservadas: Escape (salir / volver), Tab (menu), 5 (moneda), 1 (START1) y 2 (START2).
    public static bool Assignable(KeyboardKey k) =>
        k != KeyboardKey.Null && k != KeyboardKey.Escape && k != KeyboardKey.Tab && k != KeyboardKey.Five && k != KeyboardKey.One
        && k != KeyboardKey.Two;

    // Botones de salto / disparo: no la cruceta (mueve a izquierda / derecha).
    public static bool AssignableButton(GamepadButton b) => b >= GamepadButton.RightFaceUp && b <= GamepadButton.RightThumb;

    // Redefinir: si otra accion ya usaba esa tecla / boton, se intercambian (nunca dos acciones iguales).
    public void SetKey(int i, KeyboardKey k)
    {
        int j = Array.IndexOf(Keys, k);
        if (j >= 0 && j != i) Keys[j] = Keys[i];
        Keys[i] = k;
    }

    public void SetButton(int i, GamepadButton b)
    {
        int j = Array.IndexOf(Buttons, b);
        if (j >= 0 && j != i) Buttons[j] = Buttons[i];
        Buttons[i] = b;
    }

    // Estado de los controles del jugador (teclado y / o todos los joysticks conectados).
    public (bool left, bool right, bool jump, bool fire) Read()
    {
        bool l = false, r = false, j = false, f = false;
        if (Keyboard)
        {
            l = Raylib.IsKeyDown(Keys[0]); r = Raylib.IsKeyDown(Keys[1]);
            j = Raylib.IsKeyDown(Keys[2]); f = Raylib.IsKeyDown(Keys[3]);
        }
        if (Joystick)
            for (int p = 0; p < MaxPads; p++)
            {
                if (!Raylib.IsGamepadAvailable(p)) continue;
                float ax = Raylib.GetGamepadAxisMovement(p, GamepadAxis.LeftX);
                l |= Raylib.IsGamepadButtonDown(p, GamepadButton.LeftFaceLeft) || ax < -AxisDead;
                r |= Raylib.IsGamepadButtonDown(p, GamepadButton.LeftFaceRight) || ax > AxisDead;
                j |= Raylib.IsGamepadButtonDown(p, Buttons[0]);
                f |= Raylib.IsGamepadButtonDown(p, Buttons[1]);
            }
        return (l, r, j, f);
    }

    // Joysticks conectados (raylib: hasta 4), numerados #1, #2... en orden.
    public static List<JoyInfo> Detect()
    {
        var list = new List<JoyInfo>();
        for (int p = 0; p < MaxPads; p++)
        {
            if (!Raylib.IsGamepadAvailable(p)) continue;
            string name;
            unsafe { name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)Raylib.GetGamepadName(p)) ?? ""; }
            list.Add(new JoyInfo(p, list.Count + 1, name, BrandOf(name)));
        }
        return list;
    }

    // Marca segun el nombre que da raylib (GLFW: nombre del producto en DirectInput; "Xbox Controller" en
    // XInput). raylib no expone ClassId / VID del dispositivo, asi que se usa el nombre.
    public static JoyBrand BrandOf(string name)
    {
        string n = name.ToUpperInvariant();
        if (n.Contains("SONY") || n.Contains("PLAYSTATION") || n.Contains("DUALSHOCK") || n.Contains("DUALSENSE")
            || n.Contains("PS3") || n.Contains("PS4") || n.Contains("PS5") || n == "WIRELESS CONTROLLER") return JoyBrand.Sony;
        if (n.Contains("XBOX") || n.Contains("X-BOX") || n.Contains("XINPUT") || n.Contains("MICROSOFT")) return JoyBrand.Microsoft;
        return JoyBrand.Compatible;
    }

    public static string BrandText(JoyBrand b) => b switch
    {
        JoyBrand.Sony => "Sony",
        JoyBrand.Microsoft => "Microsoft",
        _ => "Compatible",
    };

    // Nombre legible de una tecla (fuente del juego: ASCII $20-$7E).
    public static string KeyText(KeyboardKey k)
    {
        if (k >= KeyboardKey.Zero && k <= KeyboardKey.Nine) return ((char)('0' + (k - KeyboardKey.Zero))).ToString();
        if (k >= KeyboardKey.Kp0 && k <= KeyboardKey.Kp9) return "Num " + (char)('0' + (k - KeyboardKey.Kp0));
        switch (k)
        {
            case KeyboardKey.LeftControl: return "Left Ctrl";
            case KeyboardKey.RightControl: return "Right Ctrl";
            case KeyboardKey.KpDecimal: return "Num .";
            case KeyboardKey.KpDivide: return "Num /";
            case KeyboardKey.KpMultiply: return "Num *";
            case KeyboardKey.KpSubtract: return "Num -";
            case KeyboardKey.KpAdd: return "Num +";
            case KeyboardKey.KpEnter: return "Num Enter";
            case KeyboardKey.KpEqual: return "Num =";
            case KeyboardKey.Apostrophe: return "'";
            case KeyboardKey.Comma: return ",";
            case KeyboardKey.Minus: return "-";
            case KeyboardKey.Period: return ".";
            case KeyboardKey.Slash: return "/";
            case KeyboardKey.Semicolon: return ";";
            case KeyboardKey.Equal: return "=";
            case KeyboardKey.LeftBracket: return "[";
            case KeyboardKey.RightBracket: return "]";
        }
        if (!Enum.IsDefined(k)) return $"Key {(int)k}";
        string s = k.ToString(), o = "";
        for (int i = 0; i < s.Length; i++) o += (i > 0 && char.IsUpper(s[i]) ? " " : "") + s[i];   // PageUp -> Page Up
        return o;
    }

    // Nombre del boton segun la marca del mando (Compatible: posicion).
    public static string ButtonText(GamepadButton b, JoyBrand brand) => brand switch
    {
        JoyBrand.Sony => b switch
        {
            GamepadButton.RightFaceUp => "Triangle", GamepadButton.RightFaceRight => "Circle",
            GamepadButton.RightFaceDown => "Cross", GamepadButton.RightFaceLeft => "Square",
            GamepadButton.LeftTrigger1 => "L1", GamepadButton.LeftTrigger2 => "L2",
            GamepadButton.RightTrigger1 => "R1", GamepadButton.RightTrigger2 => "R2",
            GamepadButton.MiddleLeft => "Share", GamepadButton.Middle => "PS", GamepadButton.MiddleRight => "Options",
            GamepadButton.LeftThumb => "L3", GamepadButton.RightThumb => "R3", _ => b.ToString(),
        },
        JoyBrand.Microsoft => b switch
        {
            GamepadButton.RightFaceUp => "Y", GamepadButton.RightFaceRight => "B",
            GamepadButton.RightFaceDown => "A", GamepadButton.RightFaceLeft => "X",
            GamepadButton.LeftTrigger1 => "LB", GamepadButton.LeftTrigger2 => "LT",
            GamepadButton.RightTrigger1 => "RB", GamepadButton.RightTrigger2 => "RT",
            GamepadButton.MiddleLeft => "View", GamepadButton.Middle => "Guide", GamepadButton.MiddleRight => "Menu",
            GamepadButton.LeftThumb => "LS", GamepadButton.RightThumb => "RS", _ => b.ToString(),
        },
        _ => b switch
        {
            GamepadButton.RightFaceUp => "Top Btn", GamepadButton.RightFaceRight => "Right Btn",
            GamepadButton.RightFaceDown => "Bottom Btn", GamepadButton.RightFaceLeft => "Left Btn",
            GamepadButton.LeftTrigger1 => "L1", GamepadButton.LeftTrigger2 => "L2",
            GamepadButton.RightTrigger1 => "R1", GamepadButton.RightTrigger2 => "R2",
            GamepadButton.MiddleLeft => "Select", GamepadButton.Middle => "Home", GamepadButton.MiddleRight => "Start",
            GamepadButton.LeftThumb => "L3", GamepadButton.RightThumb => "R3", _ => b.ToString(),
        },
    };
}
