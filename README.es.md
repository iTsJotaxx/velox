<p align="center">
  <img src="docs/logo.png" width="96" alt="Logotipo de Velox">
</p>

<h1 align="center">Velox</h1>

<p align="center">
  <b>Optimizador de Windows rápido, seguro y reversible para jugar y para el día a día.</b><br>
  Cada cambio se guarda y se puede deshacer con un clic.
</p>

<p align="center"><a href="README.md">Read in English</a></p>

![Resumen de Velox](docs/screenshots/overview.png)

## Qué hace

- **Escanea tu equipo** (CPU, gráfica, RAM, pantallas, discos, plan de energía, seguridad de Windows) y **recomienda** la configuración adecuada para *tu* hardware: portátil o sobremesa, SSD o disco duro, gráfica moderna o antigua...
- **37 optimizaciones en 7 secciones desplegables**: juegos y FPS (frecuencia del monitor, optimizaciones de pantalla completa, MPO, HAGS, MMCSS...), energía y procesador, segundo plano y RAM, red, ratón y teclado, e interfaz de Windows. Dentro de cada sección, lo que más se nota va arriba y lo que tiene riesgo al final.
- **Ajustes peligrosos marcados**: los que ganan FPS a costa de seguridad o de funciones de Windows (integridad de memoria, mitigaciones de Spectre/Meltdown, Windows Search) llevan la etiqueta roja *Riesgo*, nunca entran en el modo Recomendado y al activarlos Velox explica qué pierdes y pide confirmación.
- **Modo Recomendado o Personalizado**: aplica la configuración recomendada con un clic o elige cada interruptor. Al volver a *Recomendado* siempre recuperas la configuración recomendada. Los ajustes que en tu equipo son cuestión de gusto (o tienen un efecto mínimo) se marcan *A tu gusto* y el modo Recomendado no los toca.
- **Tres preguntas de primer uso** (portátil, juegos competitivos, grabar con OBS) afinan lo recomendado; se pueden repetir desde Ajustes.
- **Modo portátil**: ajustes que Windows aplica solo con batería (sin turbo, ahorro de energía al 30 %), aunque Velox esté cerrado.
- **Cada interruptor muestra el estado real de Windows** y no cambia nada hasta que pulsas *Aplicar cambios*.
- **Seguro**: guarda una copia de cada valor antes de cambiarlo, crea un punto de restauración antes del primer cambio y deshace una optimización si solo se aplica a medias.
- **Historial de cambios**: qué cambió Velox, cuándo, de qué valor a cuál y quién lo pidió, con *Deshacer* uno a uno.
- **Aviso de ajustes revertidos**: si una actualización de Windows u otro programa devuelve un ajuste a su valor base, el Resumen lo dice y ofrece volver a aplicarlo. Tras reiniciar, Velox comprueba que lo que pedía reinicio sigue aplicado (y, en la integridad de memoria, que Windows la tiene de verdad apagada).
- **Perfiles, exportar e importar**: guarda configuraciones con nombre y exporta un archivo con tus ajustes y las apps instaladas del catálogo: el kit para dejar igual otro equipo o este después de formatear. Al importar solo se aplica lo que cambia, con la confirmación de siempre.
- **Modo sin ventana** para automatizar: `Velox.exe --apply-recommended`, `--import=archivo.velox.json` o `--profile=nombre` (nunca aplica ajustes de riesgo sin `--include-risky`; códigos de salida 0/1/2/3, `--help` para verlos).
- **Monitor en vivo** de CPU, RAM, GPU y VRAM (funciona aunque los contadores de rendimiento de Windows estén desactivados).
- **Drivers solo de fuentes oficiales**: Windows Update (drivers certificados por Microsoft y emparejados con cada dispositivo por su identificador de hardware) y el servidor de NVIDIA para gráficas GeForce (modelo exacto y firma de NVIDIA verificada antes de ejecutar). Nunca BIOS ni firmware, nunca versiones anteriores, punto de restauración antes, caché de shaders limpiada tras actualizar la gráfica (una caché dañada puede cerrar juegos) y botón para volver al driver anterior en cada dispositivo (también con punto de restauración antes). No toca drivers si Windows tiene un reinicio pendiente ni en un portátil con batería, y las descargas tienen tiempo límite y se pueden cancelar. Con gráficas AMD e Intel se abre su herramienta oficial.
- **Inicio de Windows**: activa o desactiva programas de inicio (igual que el Administrador de tareas).
- **Procesos y juegos**: cierra programas, sube la prioridad u optimiza un juego para siempre (prioridad alta y GPU de alto rendimiento cada vez que se abre).
- **Instalar aplicaciones** (estilo Ninite): marca aplicaciones de un catálogo de 85 programas populares en 12 categorías (asistentes de IA como Claude, ChatGPT o Gemini; complementos de IA como Obsidian, Claude Code, Ollama o ComfyUI; mensajería con WhatsApp e Instagram de la Microsoft Store; juegos como League of Legends y VALORANT, que piden confirmar la región del servidor, preseleccionada según el país configurado en Windows...) y Velox las instala una tras otra, en silencio, sin barras de herramientas ni ofertas; las que ya tienes se actualizan. Además avisa de las apps instaladas que tienen versión nueva (cualquiera que conozca winget) y las actualiza en silencio; los runtimes quedan sin marcar. Los instaladores vienen de la web oficial de cada programa a través del gestor de paquetes de Windows (winget), que comprueba el SHA-256 de cada instalador antes de ejecutarlo. Puedes añadir a «Mis aplicaciones» cualquiera de las más de 10 000 del repositorio oficial.
- **Desinstalar aplicaciones**: una versión más sencilla de la lista de Windows: busca, marca y desinstala varios programas de una vez, también las apps de Microsoft Store. Los que lo permiten (MSI, Inno Setup, desinstalación silenciosa) se quitan sin ventanas; los demás abren su propio desinstalador. Las partes de Windows, los componentes que necesitan otros programas (Visual C++, .NET, DirectX, WebView2...) y los drivers están protegidos y no se pueden desinstalar; las carpetas que quedan se pueden enviar a la papelera.
- **Diagnósticos de Windows**: un *informe de salud* por áreas (disco, discos, memoria, optimizaciones, reinicios, inicio, antivirus) con acceso a dónde arreglar cada cosa; SFC, DISM, comprobación de disco, salud de discos, prueba de memoria, DirectX, informes de batería y energía, monitor de confiabilidad, ping y jitter, información de red, vaciar DNS y restablecer la red.
- **Memoria RAM**: libera RAM sin cerrar ningún programa (memoria de los programas, caché de archivos, listas en espera y modificada, combinar páginas), recorta un programa concreto, limpieza automática opcional mientras Velox está abierto y desglose en vivo de memoria en uso, modificada, en espera y libre.
- **Espacio en disco**: mapa visual de lo que ocupa una unidad o carpeta (bloques proporcionales al tamaño y con colores por tipo de archivo, carpetas anidadas), lo que más ocupa, espacio por tipo, clic para entrar y clic derecho para abrir, mostrar en el Explorador, copiar la ruta o enviar a la papelera (las carpetas clave nunca se pueden borrar: Windows, Archivos de programa, ProgramData, tu perfil y AppData, Documentos, Descargas, Escritorio... ni ninguna carpeta que las contenga, ni la de un programa instalado).
- **Personalizar Windows**: 16 opciones útiles que Windows esconde, con interruptores instantáneos y reversibles, como *Finalizar tarea* en el clic derecho de la barra de tareas, el menú contextual clásico, ver extensiones y archivos ocultos, modo oscuro o quitar la espera de los programas de inicio.
- **Herramientas**: limpieza profunda (temporales, descargas de Windows Update, optimización de distribución, cachés de navegadores cerrados y de sombreadores, miniaturas e informes de errores: mide primero y borra solo lo que marques), DNS en un clic (mide Cloudflare, Google y Quad9 desde tu conexión, cambia y vuelve al original), punto de restauración, liberar RAM, medidor de Hz del ratón, reiniciar el Explorador y deshacer todo.
- **Actualizador integrado** (Ajustes): comprueba la última release de GitHub, descarga `Velox.exe`, verifica su SHA-256 y que sea de verdad un Velox más nuevo, se reemplaza y se reinicia.
- **Español e inglés**, se cambia al instante.
- Un solo `.exe` pequeño que funciona en cualquier Windows 10/11 sin instalar nada.

## Lo que Velox *no* hace

- No toca Defender, UAC ni el firewall. La integridad de memoria y las mitigaciones del procesador solo se desactivan si tú lo eliges, con la etiqueta *Riesgo* y un aviso antes, y se reactivan con el mismo interruptor.
- No desactiva Windows Update ni desinstala aplicaciones del sistema.
- No cambia la frecuencia de sondeo del ratón: la fija el propio ratón. El medidor te dice cuánta tienes.
- Sin «trucos» placebo (`LargeSystemCache`, `Win32PrioritySeparation`, tamaño de cola del ratón, etc.), y nada se recomienda si no ayuda de verdad en tu hardware. Los ajustes de poco impacto lo dicen en su descripción, y algunos se marcan expresamente como *no recomendados* (por ejemplo desactivar Power Throttling, que puede empeorar en procesadores con núcleos de eficiencia).

## Descargar y usar

1. Descarga `Velox.exe` de la [última versión](https://github.com/iTsJotaxx/velox/releases/latest).
2. Ábrelo y acepta el aviso de administrador (Velox cambia ajustes del sistema, así que siempre se ejecuta elevado).
3. La primera vez SmartScreen puede avisar de una app sin firmar: elige *Más información → Ejecutar de todas formas*.

Requisitos: Windows 10 u 11 de 64 bits. .NET Framework 4.8 ya viene con Windows.

## Actualizar

Abre *Ajustes → Actualizar Velox*. Velox solo descarga de las releases oficiales de este repositorio, comprueba la suma SHA-256 publicada junto al archivo, que el archivo sea Velox y más nuevo que el instalado, y entonces se reemplaza y se reinicia. Las versiones anteriores a la 1.2.0 no tienen actualizador: descarga la 1.2.0 una vez desde la página de releases.

## Cómo se deshacen los cambios

Antes de cambiar nada, Velox guarda el valor original en `C:\ProgramData\Velox\backup.xml`: valores del registro (también los que no existían), tipo de inicio de servicios, tareas programadas, ajustes del plan de energía, frecuencia de la pantalla y más. Al apagar un interruptor se restaura exactamente lo que tenías. *Herramientas → Deshacer todo* lo restaura todo de una vez. Todo queda anotado en `C:\ProgramData\Velox\velox.log`.

## Compilar

```powershell
git clone https://github.com/iTsJotaxx/velox.git
cd velox
.\build.ps1            # compilación Release -> dist\Velox.exe
```

Necesitas el [SDK de .NET](https://dotnet.microsoft.com/download) 8 o superior. La app usa .NET Framework 4.8, así que el resultado no necesita instalar nada.

## Licencia

[MIT](LICENSE)
