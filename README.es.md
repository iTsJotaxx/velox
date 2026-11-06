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
- **31 optimizaciones en 6 secciones desplegables**: energía y procesador, juegos y FPS, ratón y teclado, red, segundo plano y RAM, e interfaz de Windows.
- **Modo Recomendado o Personalizado**: aplica la configuración recomendada con un clic o elige cada interruptor. Al volver a *Recomendado* siempre recuperas la configuración recomendada. Los ajustes que en tu equipo son cuestión de gusto (o tienen un efecto mínimo) se marcan *A tu gusto* y el modo Recomendado no los toca.
- **Cada interruptor muestra el estado real de Windows** y no cambia nada hasta que pulsas *Aplicar cambios*.
- **Seguro**: guarda una copia de cada valor antes de cambiarlo, crea un punto de restauración antes del primer cambio y deshace una optimización si solo se aplica a medias.
- **Monitor en vivo** de CPU, RAM, GPU y VRAM (funciona aunque los contadores de rendimiento de Windows estén desactivados).
- **Drivers solo de fuentes oficiales**: Windows Update (drivers certificados por Microsoft y emparejados con cada dispositivo por su identificador de hardware) y el servidor de NVIDIA para gráficas GeForce (modelo exacto y firma de NVIDIA verificada antes de ejecutar). Nunca BIOS ni firmware, nunca versiones anteriores, punto de restauración antes, caché de shaders limpiada tras actualizar la gráfica (una caché dañada puede cerrar juegos) y botón para volver al driver anterior en cada dispositivo. Con gráficas AMD e Intel se abre su herramienta oficial.
- **Inicio de Windows**: activa o desactiva programas de inicio (igual que el Administrador de tareas).
- **Procesos y juegos**: cierra programas, sube la prioridad u optimiza un juego para siempre (prioridad alta y GPU de alto rendimiento cada vez que se abre).
- **Diagnósticos de Windows**: SFC, DISM, comprobación de disco, salud de discos, prueba de memoria, DirectX, informes de batería y energía, monitor de confiabilidad, ping y jitter, información de red, vaciar DNS y restablecer la red.
- **Herramientas**: medidor de Hz del ratón, liberar memoria en espera, limpiar temporales, punto de restauración, reiniciar el Explorador y deshacer todo.
- **Actualizador integrado** (Ajustes): comprueba la última release de GitHub, descarga `Velox.exe`, verifica su SHA-256 y que sea de verdad un Velox más nuevo, se reemplaza y se reinicia.
- **Español e inglés**, se cambia al instante.
- Un solo `.exe` pequeño que funciona en cualquier Windows 10/11 sin instalar nada.

## Lo que Velox *no* hace

- No toca la seguridad de Windows: Defender, integridad de memoria, UAC y firewall se quedan como están (si la integridad de memoria te cuesta FPS, Velox te dice dónde cambiarla).
- No desactiva Windows Update ni desinstala aplicaciones del sistema.
- No cambia la frecuencia de sondeo del ratón: la fija el propio ratón. El medidor te dice cuánta tienes.
- Sin «trucos» placebo (`LargeSystemCache`, tamaño de cola del ratón, etc.), y nada se recomienda si no ayuda de verdad en tu hardware. Los ajustes de poco impacto lo dicen en su descripción, y algunos se marcan expresamente como *no recomendados* (por ejemplo desactivar Power Throttling, que puede empeorar en procesadores con núcleos de eficiencia).

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
