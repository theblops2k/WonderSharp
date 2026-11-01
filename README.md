# WonderSharp

Port en C# de **Wonder Boy** (Sega System 1, arcade). Romset de referencia: `wboy2u`.

## Descripción

WonderSharp es una reimplementación en C# del arcade Wonder Boy. No es un emulador: no ejecuta la ROM original, sino que reproduce la lógica, los gráficos y el sonido del juego a partir de sus datos.

## Aviso: copia original necesaria

Este proyecto **no incluye los datos del juego**. Para jugar necesitas tu copia original del romset `wboy2u` (archivo `wboy2u.zip`).

## Primer inicio

1. Coloca `wboy2u.zip` en la carpeta `files`, junto al ejecutable (`files\wboy2u.zip`).
2. Si el archivo no está, al abrir el juego aparece la ventana **"No se encontró el juego original"**:
   - **EXAMINAR**: abre el diálogo de Windows para elegir el `.zip`. El programa comprueba las 18 piezas del juego (nombre, tamaño y CRC32). Si el archivo es válido, lo copia a `files\wboy2u.zip` y el juego continúa; si no, muestra un aviso y no copia nada.
   - **SALIR** (o Esc): cierra el programa sin jugar.

## Controles

### Teclado (por defecto)

| Acción | Tecla |
|---|---|
| Mover a la izquierda | Flecha izquierda |
| Mover a la derecha | Flecha derecha |
| Saltar | Alt izquierdo (`LeftAlt`) |
| Disparar | Ctrl izquierdo (`LeftControl`) |
| Moneda | 5 |
| Start 1 jugador | 1 |
| Start 2 jugadores | 2 |
| Abrir el menú de opciones | Tab |

### Joystick (por defecto)

| Acción | Control |
|---|---|
| Mover a la izquierda / derecha | Cruceta o palanca izquierda |
| Saltar | Botón `RightFaceDown` |
| Disparar | Botón `RightFaceLeft` |

Movimiento, salto y disparo se pueden redefinir en el menú Tab, apartado **CONTROLLER**. Los cambios se guardan en `Controllers.cfg`.

## Configuración

### Menú Tab

Se abre con **Tab** durante el juego y tiene cuatro apartados: **GRAPHICS**, **SOUND**, **GAME** y **CONTROLLER**. Enter entra en un apartado; Esc vuelve atrás o cierra el menú. Cada cambio se aplica al momento y se guarda.

### Config.cfg

Se crea en la carpeta del ejecutable. Formato `Clave=Valor` (no distingue mayúsculas de minúsculas). Claves verificadas en `Graphics\GameWindow.cs`:

| Clave | Valores | Defecto | Descripción |
|---|---|---|---|
| `Fullscreen` | `NO`, `ORIGINAL`, `WIDESCREEN` | `NO` | `NO`: ventana de 512x448 (2x). `ORIGINAL`: pantalla completa con la relación de aspecto original. `WIDESCREEN`: pantalla completa que muestra más columnas del mapa en los niveles |
| `Filters` | `NONE` o lista separada por comas: `BILINEAR`, `SHARPBILINEAR`, `XBR`, `SCALE3X`, `FSR`, `VIBRANCE`, `SCANLINES` | `NONE` | Filtros de pantalla |
| `OpacityBars` | 0 a 100 | 100 | Opacidad del fondo negro de las barras del HUD (recomendado: 75) |
| `ShowFps` | `T` / `F` | `T` | Muestra los fps en el título de la ventana (y en pantalla con `ORIGINAL` o `WIDESCREEN`) |
| `MaxFPS` | 60 a 1000 | 60 | Fotogramas por segundo de la ventana. La lógica del juego sigue a 60 pasos por segundo |
| `VSync` | `T` / `F` | `F` | Sincronía vertical con el monitor |
| `SoundVolume` | 0 a 100 | 80 | Volumen del juego |
| `LivesAtStart` | 1 a 6 | 3 | Vidas al empezar cada partida |
| `InfinityCredit` | `T` / `F` | `F` | Sin monedas: START pone el crédito |
| `CorrectLicensed` | `T` / `F` | `F` | Corrige el texto "LISENCED BY" a "LICENSED BY" |

Claves ocultas (no aparecen en el menú), solo activas en el modo `--play`:

| Clave | Valores | Descripción |
|---|---|---|
| `LevelStart` | 1 a 32 | Empieza en la ronda indicada |
| `CheckPoint` | 1 a 4 | Empieza en el cartel indicado |

### Controllers.cfg

Se crea en la carpeta del ejecutable. Claves: `Keyboard`, `Joystick`, `KeyLeft`, `KeyRight`, `KeyJump`, `KeyFire`, `JoyJump` y `JoyFire`. Los nombres de teclas y botones son los de Raylib-cs (`KeyboardKey` y `GamepadButton`). Un valor no reconocido vuelve al valor por defecto.

## Compilar

Requisito: **.NET 10 SDK**.

```
dotnet build -c Release
```

El ejecutable queda en `bin\Release\net10.0\WonderBoy.exe`. Para jugar, coloca `files\wboy2u.zip` junto a ese ejecutable (ver "Primer inicio").

## Créditos

- [raylib](https://www.raylib.com/): biblioteca de ventana, gráficos, audio y entrada (licencia zlib).
- [Raylib-cs](https://github.com/ChrisDill/Raylib-cs): enlace de raylib para C# (licencia zlib), versión 8.1.0 según `WonderBoy.csproj`.

## Diferencias con el original

Lista detallada de diferencias de comportamiento respecto a la máquina arcade: **Por confirmar**.

Son propias del port: las opciones de pantalla (`Fullscreen`, incluido el modo `WIDESCREEN`), los filtros, `MaxFPS` y `VSync`.

El modo `WIDESCREEN` obliga a modificar parte de la lógica, porque si no el juego se vería mal en algunos aspectos. Para la experiencia original completa, juega con `Fullscreen=ORIGINAL` (opción "Fullscreen" del menú).

## Desarrollo propio

El código está organizado en carpetas:

| Carpeta | Contenido |
|---|---|
| `Game\` | Lógica del juego |
| `Graphics\` | Ventana, menús, filtros y render |
| `Audio\` | Sonido |
| `Input\` | Controles |
| `Icon\` | Icono |
