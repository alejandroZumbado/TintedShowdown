# QuickLobby — Netcode Relay Kit (asset a vender)

> Side-project derivado de este mismo juego: extraer el stack de networking
> (NGO + Unity Relay + WebGL, con los bugs reales ya resueltos — ver
> `PROJECT_OVERVIEW.md`) y venderlo como paquete Unity independiente. No depende
> de que Tinted Showdown tenga jugadores o marketing — monetiza el trabajo técnico ya hecho.

## Ubicaciones

- **Repo (privado):** https://github.com/alejandroZumbado/QuickLobby-NetcodeRelayKit
- **Código local original:** `C:\Users\User\Desktop\RelayLobbyKit`
- **Copia de respaldo (hecha 2026-08-18 tras la pérdida del checkout del juego):**
  `E:\Users\Alejandro\Opal\QuickLobby-NetcodeRelayKit` — copia fiel del working tree,
  incluye el `.git` (branch `master`, up to date con `origin/master`) y los 3 fixes
  de code review sin commitear (ver abajo).
- **Proyecto de prueba interactivo:** `C:\Users\User\Desktop\QuickLobbyTestProject`
  (Library generada, 0 errores CS, referencia el paquete vía `file:` — cambios al
  código del asset se reflejan al instante). Guía: `COMO_PROBAR.md` en esa carpeta.
  **No se copió a E: — sigue solo en C:.**

## Qué es

Paquete UPM genérico `com.quicklobby.netcoderelay`, extraído y generalizado de
`SessionNetworkManager`/`LobbyNetwork`/parte de `GameManager` de Tinted Showdown —
sin ninguna referencia al juego original (nombres, namespace, tags de log, todo renombrado).

**Clases:**
- `RelayLobbyManager` — crear/unirse sala, selección de endpoint (mismo fix de
  `ConnectionType` documentado en `PROJECT_OVERVIEW.md`), min/max jugadores configurables,
  `CurrentPlayers`, `MinPlayersReached`, evento `OnMinPlayersReached`, `StartGameNow()`
- `LobbySyncNetwork` — sincronización de conteo de jugadores (patrón ClientRpc)
- `NetcodePlayerSpawner`
- `RelayLobbyConfig` (ScriptableObject)

UI-agnostic — solo eventos C#, sin paneles propios. Agrega sobre el original: contraseña
de sala real (vía NGO Connection Approval, separada del join code), override de
connectionType, auto-load de escena configurable.

## Decisiones tomadas

- **Marketplace:** Fab.com primero (split 88/12, sin requisito de sitio propio, decisión
  2026-07-30). Unity Asset Store queda para después, usando el build de Tinted Showdown
  como portfolio si se persigue.
- **Precio de lanzamiento: $9.99** (confirmado 2026-08-11 — piso recomendado; precios
  más bajos leen como "hobby/sin soporte" en marketplaces de assets, contradice el
  diferenciador de "fix de bug de producción real". Sin competidor pago directo en el
  nicho de "solo lobby/relay kit"; el sample oficial de Unity es gratis pero básico,
  templates completos de juego con Netcode+Lobby+Relay rondan $35 pero son juegos enteros).
- **Dependencia Relay vs Multiplayer SDK:** `com.unity.services.relay` está marcado
  deprecado por Unity a favor de `com.unity.services.multiplayer`, pero se decidió
  seguir con `com.unity.services.relay` para v1.0.0 (liviano, funciona hoy). Migración
  al SDK unificado queda en roadmap, no urgente.
  **Gotcha documentado en el manual del asset:** tener ambos paquetes instalados a la
  vez rompe la compilación de TODO el proyecto con `CS0433` (definen los mismos tipos
  bajo el mismo namespace) — no solo el código del asset.
- **Política de IA:** tanto Fab como Unity Asset Store permiten assets hechos con
  asistencia de IA, pero exigen declararlo. Texto de disclosure ya redactado.
- **Publisher:** Mandrix Studio (fijado en `package.json`/`LICENSE.md`, commit `78e0386`).
  El repo vive en la cuenta personal de GitHub (`alejandroZumbado`), no en una
  organización — mismatch de marca menor, sin decidir todavía.

## Code review completo (2026-08-11) — 3 hallazgos, los 3 ARREGLADOS pero SIN COMMITEAR

Working tree de `RelayLobbyKit` (y por lo tanto de la copia en E:) sigue modificado.
**Primer paso al retomar: revisar y confirmar este commit.**

1. **`minPlayers` era un campo muerto** — expuesto en Inspector, documentado, anunciado
   como feature, pero nunca leído en código (el arranque solo miraba `maxPlayers`).
   Fix: `RelayLobbyManager` ahora expone `CurrentPlayers`, `MinPlayersReached`, evento
   `OnMinPlayersReached`, método `StartGameNow()`. Refactor: `OnClientConnected`/
   `HandleDisconnect`/`HandleSyncCountChanged` pasan por un helper común `UpdateCount()`.
2. **`package.json` no declaraba** `com.unity.services.core`/`authentication`/`relay`
   como dependencies (el asmdef sí las referencia) — instalación limpia no las
   auto-resolvía. Fix: agregadas (core 1.16.0, authentication 3.6.1, relay 1.1.1,
   versiones verificadas compilando en `QuickLobbyTestProject`).
3. **`LICENSE.md` tenía un "TODO before publishing"** sin resolver, visible para el
   comprador. Fix: reemplazado por texto que aclara Fab Content License Agreement + copyright.

Documentación (`manual.md`, `README.md`, `CHANGELOG.md`) ya actualizada para reflejar el fix #1.

## Barrido de "profesionalismo" (2026-08-11)

Búsqueda en todo el repo de referencias a Tinted Showdown, paths/nombres personales,
jerga informal (hack/WIP/TODO), nombres de clases del juego original filtrados.
**Resultado: repo limpio, nada que sacar.**

## Pendiente — bloqueado en el usuario

- [ ] Revisar y confirmar el commit de los 3 fixes de arriba
- [ ] Decidir mismatch de marca del repo (dejarlo en cuenta personal vs. crear org de GitHub)
- [ ] Probar la demo interactivamente en Editor (`COMO_PROBAR.md`) — requiere vincular
      Unity Gaming Services en `QuickLobbyTestProject` (`Project Settings → Services`),
      paso que pide cuenta/dashboard del usuario. `ProjectSettings.asset` todavía sin
      `cloudProjectId` a fecha 2026-08-11 — sigue sin vincular.
- [ ] Crear cuenta de publisher en Fab.com (pide datos de pago/identidad)

## Pendiente — para hacer una vez destrabado lo de arriba

- [ ] Screenshots/GIF reales de la demo funcionando (necesita verse corriendo en el Editor)
- [ ] Ícono/logo del paquete
