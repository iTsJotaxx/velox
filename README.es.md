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
- **Modo Recomendado o Personalizado**: aplica la configuración recomendada con un clic o elige cada interruptor. Al volver a *Recomendado* siempre recuperas la configuración recomendada.
- **Cada interruptor muestra el estado real de Windows** y no cambia nada hasta que pulsas *Aplicar cambios*.
- **Seguro**: guarda una copia de cada valor antes de cambiarlo, crea un punto de restauración antes del primer cambio y deshace una optimización si solo se aplica a medias.
- **Monitor en vivo** de CPU, RAM, GPU y VRAM (funciona aunque los contadores de rendimiento de Windows estén desactivados).
- **Inicio de Windows**: activa o desactiva programas de inicio (igual que el Administrador de tareas).
- **Procesos y juegos**: cierra programas, sube la prioridad u optimiza un juego para siempre (prioridad alta y GPU de alto rendimiento cada vez que se abre).
- **Diagnósticos de Windows**: SFC, DISM, comprobación de disco, salud de discos, prueba de memoria, DirectX, informes de batería y energía, monitor de confiabilidad, ping y jitter, información de red, vaciar DNS y restablecer la red.
- **Herramientas**: medidor de Hz del ratón, liberar memoria en espera, limpiar temporales, punto de restauración, reiniciar el Explorador y deshacer todo.
- **Español e inglés**, se cambia al instante.
- Un solo `.exe` pequeño que funciona en cualquier Windows 10/11 sin instalar nada.

## Lo que Velox *no* hace

- No toca la seguridad de Windows: Defender, integridad de memoria, UAC y firewall se quedan como están (si la integridad de memoria te cuesta FPS, Velox te dice dónde cambiarla).
- No desactiva Windows Update ni desinstala aplicaciones del sistema.
- No cambia la frecuencia de sondeo del ratón: la fija el propio ratón. El medidor te dice cuánta tienes.
- Sin «trucos» placebo (`LargeSystemCache`, tamaño de cola del ratón, etc.).

## Descargar y usar

1. Descarga `Velox.exe` de la [última versión](https://github.com/iTsJotaxx/velox/releases/latest).
2. Ábrelo y acepta el aviso de administrador (Velox cambia ajustes del sistema, así que siempre se ejecuta elevado).
3. La primera vez SmartScreen puede avisar de una app sin firmar: elige *Más información → Ejecutar de todas formas*.

Requisitos: Windows 10 u 11 de 64 bits. .NET Framework 4.8 ya viene con Windows.

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
