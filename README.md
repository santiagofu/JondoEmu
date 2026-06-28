# Jondo Unity Emulator - Dofus 3.6.4.3

Este repositorio contiene la arquitectura completa del emulador local **Jondo** diseñado para el cliente Unity de Dofus (versión 3.6.4.3), así como el mod cliente **JondoFix** para MelonLoader.

## 📊 Estado de la Emulación

### ✅ Funcionalidades Completadas / Emuladas
- [x] **Emulación cliente-servidor-autenticación** (Zaap, HAAPI, Connection Server)
- [x] **Elección de servidor**
- [x] **Elección de personaje**
- [x] **Carga de mundo (World / Game Node)**
- [x] **Spawn del personaje**
- [x] **Hover del nombre del personaje**
- [x] **Movimiento**
- [x] **Persistencia de la última casilla y mapa** en base de datos
- [x] **Cambio de mapa**
- [x] **Carga del mapa**
- [x] **Cálculo de mapas adyacentes**

### 🚧 En Desarrollo (Work In Progress)
- [ ] **Sistema de Inventario**
- [ ] **Características del personaje (stats)**
- [ ] **NPCs y diálogos**

---


## 📂 Estructura del Repositorio

* **`Jondo Emulator Launcher.exe`** (en la raíz): Ejecutable compilado listo para iniciar el servidor del emulador local (con todas sus DLLs dependientes y la carpeta `runtimes/`).
* **`JondoFix.dll`** (en la raíz): Binario precompilado del mod cliente de MelonLoader listo para ser utilizado en el juego.
* **[Jondo.Unity.sln](file:///C:/Jondo/Jondo%20Unity%20Emulator/Jondo.Unity.sln)**: Solución de Visual Studio que agrupa todos los subproyectos del emulador:
  * **[Jondo.Unity.Launcher](file:///C:/Jondo/Jondo%20Unity%20Emulator/Jondo.Unity.Launcher)**: Punto de entrada del servidor, proxies, parser de red y base de datos local.
  * **[Jondo.Unity.Core](file:///C:/Jondo/Jondo%20Unity%20Emulator/Jondo.Unity.Core)**: Capa de red básica y servidores TCP.
  * **[Jondo.Unity.Auth](file:///C:/Jondo/Jondo%20Unity%20Emulator/Jondo.Unity.Auth)**: Servicio de autenticación.
  * **[Jondo.Unity.Protocol](file:///C:/Jondo/Jondo%20Unity%20Emulator/Jondo.Unity.Protocol)**: Definiciones y buffers de protocolo (Protobuf).
  * **[Jondo.Unity.World](file:///C:/Jondo/Jondo%20Unity%20Emulator/Jondo.Unity.World)**: Lógica del Game Node / servidor de mundo.
* **[JondoFix](file:///C:/Jondo/Jondo%20Unity%20Emulator/JondoFix)**: Código fuente del mod de MelonLoader que redirecciona el cliente de Dofus hacia el servidor local y elude el chequeo de certificados SSL oficiales.
* **[Dofus3 Defuscated Data](file:///C:/Jondo/Jondo%20Unity%20Emulator/Dofus3%20Defuscated%20Data)**: Dump de clases desofuscadas, encabezados de IL2CPP y scripts de análisis para Ghidra e IDA.
* **[EspecificacionTecnica.md](file:///C:/Jondo/Jondo%20Unity%20Emulator/EspecificacionTecnica.md)**: Especificación detallada del protocolo, arquitectura de red, puertos y el doble rol sniffer en el puerto `5555`.

---

## 🚀 Guía de Inicio Rápido (Sin Compilar)

Cualquier persona puede clonar el repositorio y arrancar el emulador de inmediato con los archivos precompilados de la raíz.

### Paso 1: Levantar el Emulador (Servidor)
1. Ejecuta **`Jondo Emulator Launcher.exe`** en la raíz de este directorio.
2. Esto iniciará de manera local:
   - El puerto named pipe/TCP `15881` de Ankama Zaap API.
   - El servidor HTTP HAAPI en el puerto `8888`.
   - El Connection Server y Game Node en el puerto `5555`.
   - El Chat Server seguro en el puerto `6337`.

---

### Paso 2: Configurar el Cliente Dofus (MelonLoader & JondoFix)

El cliente de Dofus oficial por defecto intenta conectarse a los servidores oficiales de Ankama y verifica los certificados de seguridad SSL/TLS. Para redirigirlo de forma local y segura, utilizamos **MelonLoader** y el mod **JondoFix**.

#### 1. Instalar MelonLoader
1. Descarga el instalador de **MelonLoader** (versión `0.6.x` o compatible con .NET 6) desde su repositorio oficial en GitHub: [MelonLoader Releases](https://github.com/LavaGang/MelonLoader/releases).
2. Ejecuta el instalador y selecciona el archivo executable del cliente de Dofus (por ejemplo, `Dofus.exe` en tu carpeta de cliente de Dofus).
3. Asegúrate de configurar la instalación para que use el runtime correspondiente (normalmente se detecta automáticamente como **IL2CPP** o **.NET 6**).
4. Haz clic en **Install**. Esto creará carpetas como `MelonLoader/`, `Mods/` y `UserData/` en la carpeta del juego.

#### 2. Cargar el mod JondoFix
1. Ve a la raíz de este repositorio y copia el archivo precompilado **`JondoFix.dll`**.
2. Pégalo dentro de la carpeta **`Mods/`** que se ha generado en el directorio de instalación de tu cliente de Dofus.
3. Al iniciar el juego mediante el launcher oficial o directamente con el ejecutable modificado por MelonLoader, el mod se cargará de forma automática.

#### ¿Qué hace JondoFix exactamente?
* **Redirección de Red**: Intercepta sockets, Named Pipes y llamadas de DNS redirigiendo el tráfico web de Ankama a `localhost` (puertos `8888`, `5555`, etc.).
* **Bypass de SSL**: Evita que las llamadas HTTPS hacia HAAPI y el Connection Server fallen por utilizar certificados locales autofirmados.
* **Configuración del Entorno**: Injecta las variables de entorno necesarias (`ZAAP_PORT`, `ZAAP_HASH`, etc.) para engañar al cliente simulando que el Launcher oficial está corriendo de fondo.

---

## 🛠️ Desarrollo e Hilado Fino (Compilación desde Código)

Si deseas realizar modificaciones al emulador o al mod del cliente:

### Compilar el Emulador
Puedes abrir la solución **`Jondo.Unity.sln`** en Visual Studio 2022 o compilar directamente desde tu terminal con el comando:
```bash
dotnet build -c Release
```

### Compilar JondoFix
El mod del cliente se puede compilar con:
```bash
dotnet build JondoFix/JondoFix.csproj -c Release
```
El archivo DLL resultante se generará en `JondoFix/bin/Release/net6.0/JondoFix.dll`.
