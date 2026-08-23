# Tinted Showdown — Referencia del proyecto

> Reconstruido acá el 2026-08-18 tras pérdida del checkout local en
> `C:\Users\User\Desktop\juegos unity\Tinted Showdown` (`.git` y `Assets` quedaron vacíos).
> Esta carpeta (`E:\Users\Alejandro\Opal\Tinted Showdown`) tenía una versión antigua
> (commit `a9e7715`) — se hizo `git fetch` + `git pull --ff-only` hasta `2993f20`
> (HEAD real de `origin/main`), 17 commits recuperados. Working tree limpio.
> Los 3 archivos con churn de versión de Editor (manifest.json, ProjectSettings.asset,
> ProjectVersion.txt — de haber abierto esta copia vieja con Unity 6000.3.13f1 en vez
> de 6000.1.7f1) quedaron en `git stash list` por si hacen falta, sin aplicar.

## Qué es

Juego FFA (free-for-all) para 2-4 jugadores, personajes voxel con pintura de equipo,
multiplayer online. Migrado de Photon PUN a **Netcode for GameObjects (NGO) 2.1.1 +
Unity Relay**. Publicado en WebGL vía GitHub Pages.

- **URL jugable:** https://alejandrozumbado.github.io/tinted-showdown-build/
- **Repo de código:** `git@github.com:mandrix/TintedShowdown.git`
  (la cuenta se movió a `alejandroZumbado` — el remote viejo sigue funcionando por
  redirect de GitHub, pero convendría actualizarlo con
  `git remote set-url origin git@github.com:alejandroZumbado/TintedShowdown.git`)
- **Repo del build WebGL (deploy, no es el código fuente):**
  `git@github.com:alejandroZumbado/tinted-showdown-build.git`,
  output local en `E:\Users\Alejandro\Opal\Builds\Tinten Showdown`
  (nombre de carpeta sin la "d" final — ojo, no confundir con esta carpeta del proyecto).

## Stack técnico

- Unity 6000.3.13f1 (proyecto original abierto con 6000.1.7f1, actualizado después)
- Netcode for GameObjects 2.1.1 + Unity Relay + Unity Services (auth/core/relay)
- Ambiente día/noche: paquete BOXOPHOBIC (Skybox Cubemap Extended)
- Template WebGL propio responsive: `Assets/WebGLTemplates/TintedShowdown/`

## Arquitectura / piezas clave

- `SessionNetworkManager.cs` — crear/unirse sala vía Relay, selección de endpoint
- `LobbyNetwork.cs` — sincronización del conteo de jugadores en el lobby
- `GameManager.cs` — spawnea jugadores (única fuente de verdad, ver gotcha #1 abajo),
  arranca rondas, evalúa ganador, randomiza preset de ambiente por ronda
- `ActionPlayerManager.cs` — jugador individual: color, nombre, cámara propia
- `EnvironmentManager` / `EnvironmentPresetManager` — rig de sol/skybox día-noche
- `LobbyUIManager.cs` — UI del lobby, panel de nombre, panel de espera
- `Assets/Scripts/Editor/TintedShowdownSetup.cs` — menú **"Tinted Showdown → Setup All"**,
  automatiza todo el wiring de Inspector (idempotente, no pisa ajustes manuales)
- `Assets/Scripts/Editor/WebGLBuildScript.cs` — build headless de WebGL

## Gotchas importantes (para no re-descubrirlos)

### 1. Endpoint de Unity Relay — elegir por `ConnectionType`, NO por puerto
Bug real en producción: WebGL quedaba en blanco al crear/unirse a sala porque el código
elegía el endpoint por puerto fijo (443/7777), pero Relay devuelve puertos dinámicos.
**Fix correcto:** usar `RelayServerEndpoint.ConnectionType` (`"udp"`/`"dtls"`/`"wss"`) +
`AllocationUtils.ToRelayServerData(connectionType)` (namespace `Unity.Services.Relay.Models`).

```csharp
private static string PreferredConnectionType()
{
    if (Application.platform == RuntimePlatform.WebGLPlayer) return "wss";
    if (Application.isEditor) return "udp"; // DTLS es inestable en el Editor
    return "dtls"; // standalone: UDP cifrado
}
```

También hace falta setear `transport.UseWebSockets` en runtime según plataforma
(no dejarlo fijo en el asset de la escena) justo antes de `SetRelayServerData(...)`.

Si "crear sala"/"unirse" no hace nada: revisar consola del browser buscando
`StartHost returned: False` o `ArgumentException` de `NetworkDriver.Create`.

**Trampa al testear WebGL:** GitHub Pages sirve los `.unityweb` con
`cache-control: max-age=600` — un rebuild recién publicado puede parecer "no funciona"
si el browser sirve la versión vieja de su caché. Siempre hard-reload (`ctrl+shift+r`)
antes de concluir que un fix no sirvió.

### 2. WebGL en GitHub Pages necesita `Decompression Fallback = true`
GitHub Pages no manda `Content-Encoding: gzip/br`. Sin el fallback, el build carga
perfecto en local pero queda en blanco (loader colgado, sin error visible) una vez
publicado. Ya está seteado en `TintedShowdownSetup.SetupWebGLPlayerSettings()`.

Patrón de publicación (cuenta `alejandroZumbado`): un repo público `<juego>-build` por
juego, Pages activo en la raíz de `main`. No existe (ni hace falta) un repo
`alejandroZumbado.github.io`.

### 3. Multiplayer Play Mode (MPPM) — nunca usar `static Instance` ni búsquedas por tag
Los campos `static Instance` y `GameObject.Find`/`FindWithTag`/`Camera.main` NO están
garantizados aislados entre jugadores virtuales del mismo proceso — causa bugs tipo
"el input del jugador 2 controla al jugador 1".

**Usar siempre:** `FindFirstObjectByType<T>()` / `FindObjectsByType<T>()`, o iterar
`NetworkManager.Singleton.SpawnManager.SpawnedObjects` cuando el contexto es de red.

Aplica en cualquier código dentro de `OnNetworkSpawn`, `ClientRpc`, o callbacks de botón UI.

### 4. Conteo de jugadores en lobby — `ClientRpc` explícito, no `OnValueChanged`
`NetworkVariable.OnValueChanged` tiene timing inestable en el join. Patrón usado:
el servidor llama `PushCountClientRpc(count, max)` cada vez que cambia el conteo, que
a su vez busca la UI vía `FindFirstObjectByType` (MPPM-safe). Para late-join, una
coroutine espera 1 frame antes de leer el valor sincronizado.

### 5. `NetworkConfig.PlayerPrefab` causa spawn duplicado
Con Connection Approval desactivado, si `PlayerPrefab` tiene algo asignado, NGO
auto-spawnea un jugador por cliente ADEMÁS del que `GameManager` spawnea a mano →
2 jugadores reales por cliente. `TintedShowdownSetup` lo limpia (`= null`) en cada corrida.

### 6. Overrides de `PrefabInstance` en escena pisan el wiring del prefab
Corregir el prefab asset no arregla una instancia ya colocada en una escena si esa
instancia tiene un override congelado (ej. `m_OnClick...m_Target: {fileID: 0}`).
Si un botón deja de andar después de arreglar un prefab, sospechar de esto primero.

### 7. RPCs de NGO no serializan `string[]`
Arrays solo sirven en RPCs si son blittable (`int[]`, `FixedString32Bytes[]`). Para
mandar texto variable (ej. lista de ganadores), armar el string ya formateado
server-side y mandar un solo `string`.

### 8. Nombres de jugador en red: `FixedString32Bytes`, no `string`
`NetworkVariable<string>` no existe — hace falta `Unity.Collections.FixedString32Bytes`
(~28 bytes UTF-8). Clampear a 16 caracteres tanto al guardar como al leer (tildes
ocupan 2 bytes).

## `TintedShowdownSetup.cs` — filosofía

Es **idempotente**: cada parte solo actúa si algo falta o está mal, nunca pisa un
ajuste manual ya hecho (ej. un spawn point rotado a mano). Al agregar una feature
nueva que necesite un GameObject/panel nuevo en una escena ya generada antes, usar
el patrón "Ensure" (chequear si existe → crear si no) en vez de meterlo dentro de un
bloque que solo corre "si tal cosa no existe".

Sí se fuerza siempre a propósito (son invariantes/bugfixes, no contenido del usuario):
limpieza de jugadores manuales en Arena, `PlayerPrefab = null`, lock de orientación landscape.

## Estado funcional (actualizado 2026-08-22, sesión modo solo vs bots)

- Conexión online vía Relay: funciona (Editor, standalone, WebGL — WebGL con fix aplicado y retesteado en vivo)
- Lobby: crear sala + join por código, ambiente día/noche sincronizado
- Colores body/weapon, cámara por jugador, timer, rondas, pantalla de ganadores, nombre real de jugador
- **Modo solo vs 3 bots** (nuevo, 2026-08-22, **confirmado funcionando en el build WebGL
  publicado**): botón "Jugar Solo" en el menú, crea sala Relay solo para el host (sigue
  requiriendo internet — no es offline literal, ver `## Modo Solo vs Bots` más abajo) y
  spawnea 3 bots server-side con nombres random (`GameManager.BotNamePool`, sin repetir
  por partida) que van cambiando de color en vivo (timers independientes por bot y por
  arma/armadura, ~15% de jugada tardía)
- **Historial de partidas** (nuevo, **confirmado funcionando**): botón en el menú, cuenta
  jugadas/ganadas separado para modo solo vs online (`MatchStats.cs`, PlayerPrefs local)
- **Fix de softlock** (nuevo): si quedan 0-1 jugadores conectados a mitad de partida,
  la partida termina sola y muestra la pantalla de fin con botón "Volver al Menú" (antes
  se quedaba pegada para siempre — ver pendiente de abajo, ahora resuelto). No confirmado
  en vivo todavía (requiere 2+ clientes reales para probar).
- Build WebGL público y funcionando — https://alejandrozumbado.github.io/tinted-showdown-build/,
  redeployado 2026-08-22 con todo lo de arriba (commit build `1d8dd62`, source `8a75f1d`)

**Pendiente / sin confirmar:**
- Reacomodo manual de decoración en `Arena.unity` (el auto-despeje por radio se quitó,
  el usuario iba a hacerlo a mano en el Editor — commit `2993f20` "Fix piso invisible"
  sugiere que esto ya se resolvió, pero no hay build WebGL redeployado con ese fix confirmado)
- `environmentRotationEuler` de los 3 presets — decoración y sol comparten el mismo
  Transform padre, si se ajusta rotación revisar que nada se movió hacia el área de juego
- Fix de Relay en build standalone (PC) real — nunca se testeó end-to-end, solo WebGL
- Android — sin probar
- Performance con 3-4 jugadores simultáneos
- ~~Softlock si quedan 1 o 0 jugadores conectados a mitad de partida~~ — **arreglado 2026-08-22**
- Sin límite de rondas/empate si nadie llega a 10 puntos — sigue pendiente
- Aislamiento de `PlayerPrefs` (nombre de jugador) entre Virtual Players de MPPM — sin confirmar
  (solución propuesta no implementada: sufijar la key con el argumento `-name` de MPPM)
- El flujo online (crear sala / entrar con 2+ jugadores reales) no se retesteó en vivo con
  2 clientes reales después de los cambios de esta sesión — se revisó el código a fondo
  (los cambios de `GameManager`/`LobbyUIManager`/`SessionNetworkManager` para el modo solo
  quedan detrás de `BotCountToSpawn == 0` o son aditivos) pero falta la confirmación real
- Remote de git actualizado a `alejandroZumbado/TintedShowdown` — pendiente que el usuario
  lo corra a mano (bloqueado por el harness de Claude Code, ver sesión 2026-08-22)

## Modo Solo vs Bots (2026-08-22)

Decisión tomada con el usuario: "offline" se resuelve como **solo vs bots vía Relay**, no
como modo 100% sin internet. Sigue usando la misma infraestructura UGS/Relay de siempre
(el host crea una sala para 1 jugador real y el propio servidor spawnea 3 bots), lo que
evitó duplicar toda la lógica de partida en una rama de código paralela no-networked.
Se descartó a propósito el offline literal — hubiera requerido separar pintado/cámara/
scoring de `ActionPlayerManager` en componentes no-networked, mucho más riesgo para el
mismo resultado percibido por el jugador. Detalle técnico completo en `CLAUDE.md`
(`BotController.cs`, `GameManager.RunBotsForRound`/`BotStatLoop`, `MatchStats.cs`).

## Build / deploy — flujo completo

1. Editar código acá.
2. Rebuild headless: `Unity.exe -batchmode -quit -projectPath "<esta carpeta>" -executeMethod WebGLBuildScript.Build`
   → escribe en `E:\Users\Alejandro\Opal\Builds\Tinten Showdown`
3. `cd` a esa carpeta de build → commit + push a `tinted-showdown-build` → Pages se actualiza solo
4. Aparte, commit + push del código fuente a este repo (`TintedShowdown`)

Unity Editor: `E:\versionesUnity\6000.3.13f1\Editor\Unity.exe`
