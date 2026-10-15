# Dependencias de compilación

El plugin solo necesita **PKHeX.Core.dll** de la misma compilación que el PKHeX de destino. La versión probada es 26.8.26.0; ver `dependency-lock.json`.

`build.ps1 -PKHeXDirectory <carpeta>` copia ese Core aquí y los DLL de la aplicación a `host/` para las pruebas de interfaz. Las pruebas usan la ventana y el cargador originales de PKHeX, no una simulación del menú.

No se requiere Auto-Legality Mod. Estas bibliotecas no se incluyen en el ZIP de código fuente ni deben instalarse en la carpeta `plugins`.
