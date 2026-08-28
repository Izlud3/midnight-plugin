# Midnight Timeline

Midnight Timeline es un plugin de Dalamud para revisar el timing de las acciones del jugador y analizar retrospectivamente las mecánicas de Forsaken en Dancing Mad (Ultimate).

El timeline de acciones funciona en cualquier contenido. Las herramientas de Forsaken solo se activan dentro de Dancing Mad o durante una reproducción compatible de Duty Recorder.

## Qué ofrece

- Timeline local de GCD y oGCD con iconos, clasificación y marcas de tiempo.
- Práctica de rotación con referencias compartibles, cuenta regresiva y evaluación de aciertos, fallos y acciones incorrectas.
- Referencia de PLD incluida, basada en una rotación revisada de Dancing Mad.
- Opción para detener la práctica después de tres errores.
- Historial compacto de hasta 10 pulls de Dancing Mad por sesión.
- Revisión retrospectiva de torres, stacks, conos, muertes y posiciones durante Forsaken.
- Tarjetas de fallo y una captura del campo al resolverse la mecánica.
- Diagnóstico local para investigar problemas de captura.

Midnight Timeline no predice movimiento, no muestra un radar continuo y no proporciona indicaciones durante la resolución de una mecánica.

## Instalación

Añade esta URL en `/xlsettings` → **Experimental** → **Custom Plugin Repositories**:

```text
https://raw.githubusercontent.com/Izlud3/midnight-plugin/main/pluginmaster.json
```

Guarda los cambios, abre `/xlplugins`, busca **Midnight Timeline** e instala la versión más reciente.

El plugin requiere XIVLauncher y Dalamud API 15. No requiere ACT, IINACT, BossMod, Beholder, un servidor externo ni conexión de red durante su ejecución.

## Uso

La ventana principal y la configuración están disponibles desde la interfaz de plugins de Dalamud.

| Comando | Comportamiento |
| --- | --- |
| `/midnighttimeline` | Abre o cierra el timeline de acciones. |
| `/midnighttimeline forsaken` | Abre la revisión de Forsaken. |
| `/midnighttimeline log` | Abre el diagnóstico de captura. |

## Práctica de rotación

La práctica sigue automáticamente el job equipado cuando existe una referencia compatible. Se arma al entrar en combate y comienza cuando la primera acción del jugador coincide con la acción inicial de la referencia. Permite pausar, reiniciar o comenzar desde otro punto de la rotación.

Las referencias son archivos JSON compartibles. Para añadir o reemplazar una, copia el archivo en la carpeta mostrada en **Configuración** y pulsa **Recargar referencias**. Una referencia del usuario reemplaza la incluida para el mismo job sin necesidad de modificar el plugin.

## Revisión de Forsaken

Durante Dancing Mad, el plugin conserva hasta 10 resúmenes en memoria y permite revisar qué ocurrió al resolver Forsaken. Las vistas utilizan información ya observada por el cliente para representar posiciones y resultados después de la resolución.

## Privacidad

Midnight Timeline no realiza solicitudes web ni consulta bases de datos durante su ejecución.

El historial de pulls permanece en memoria y se elimina al cerrar sesión o recargar el plugin. El registro de diagnóstico se guarda localmente y se puede limpiar desde su ventana.

## Licencia

Midnight Timeline se distribuye bajo la [licencia MIT](LICENSE).
