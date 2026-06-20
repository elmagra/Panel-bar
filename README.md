# Panel Tray Launcher

Mini launcher personal para Windows 11 con icono en la bandeja del sistema, panel visual por categorias, deteccion de procesos y configuracion persistente en JSON.

## Descarga (v1.0)

1. Ve a [Releases](https://github.com/mmyzx/Panel-bar/releases/latest).
2. Descarga `PanelTray-v1.0.0-win-x64.zip`.
3. Extrae el zip y ejecuta `PanelTray.exe`.
4. Abre el panel con el icono de la bandeja o con la hotkey por defecto (`Ctrl+Alt+P`).

No hace falta instalar .NET por separado: el zip incluye el runtime.

## Tecnologia

- C# / .NET 8 / WPF
- Icono de bandeja mediante `System.Windows.Forms.NotifyIcon` integrado en WPF
- Persistencia con `System.Text.Json`
- Configuracion en `%APPDATA%\MiPanelTray\config.json`
- Logs en `%APPDATA%\MiPanelTray\logs`

## Funciones incluidas

- Panel pequeno y visual abierto desde la bandeja.
- Cuadricula de tarjetas con icono, nombre y estado rojo/verde/amarillo.
- Categorias: Apps, Juegos, Sistema y Otros.
- Clic izquierdo para abrir o traer al frente.
- Clic derecho con Abrir, Informacion, Cerrar, Reiniciar, Abrir ubicacion, Editar y Eliminar.
- Anadir apps desde instaladas (Store, accesos directos del menu Inicio).
- Integracion con bandeja para apps como Tailscale (panel con exit nodes, etc.).
- Ventana de configuracion para anadir, editar y eliminar apps.
- Rutas, argumentos, nombre de proceso, icono, categoria y preferencias visuales editables.
- Tema claro/oscuro.
- Inicio con Windows mediante clave `Run` de usuario.
- Modo edicion con drag & drop y guardado automatico del orden.
- Backup automatico si el JSON de configuracion se corrompe.

## Desarrollo

Necesitas instalar el SDK de .NET 8 para compilar:

```powershell
dotnet --list-sdks
dotnet build .\PanelTray.sln
```

Publicacion single-file self-contained para Windows x64 (release):

```powershell
dotnet publish .\src\PanelTray\PanelTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

El ejecutable quedara en `publish\PanelTray.exe`.

## Notas

Algunas aplicaciones usan launchers o procesos distintos al ejecutable de arranque. Por eso cada app tiene dos campos separados: ruta de lanzamiento y nombre de proceso a detectar.
