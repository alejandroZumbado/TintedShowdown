# Estado y ubicaciones — post recuperación (2026-08-18)

> **Decisión (2026-08-18):** de ahora en adelante se trabaja desde las carpetas en
> `E:\`. `E:\Users\Alejandro\Opal\Tinted Showdown` y
> `E:\Users\Alejandro\Opal\QuickLobby-NetcodeRelayKit` son las copias activas.
> Ambas tienen su propio `CLAUDE.md` en la raíz (leído automáticamente por Claude
> Code al abrir cualquiera de las dos en VS Code) que explica qué es cada proyecto,
> dónde está el otro, y linkea a `docs/` para el detalle completo. Las copias en
> `C:\` (juego roto, `RelayLobbyKit`, `QuickLobbyTestProject`) quedan como están
> por ahora — no se tocan ni se borran salvo pedido explícito.

## Qué pasó

La carpeta `C:\Users\User\Desktop\juegos unity\Tinted Showdown` perdió su `.git` y
`Assets` quedó vacío (0 archivos) — causa desconocida. El código fuente NO se perdió
(seguía íntegro en GitHub). Esta carpeta (`E:\Users\Alejandro\Opal\Tinted Showdown`)
tenía una copia de trabajo antigua (commit `a9e7715`, versión con Photon todavía,
pre-migración a NGO) que se actualizó con `git pull --ff-only` hasta `2993f20`
(HEAD de `origin/main`, 17 commits recuperados). **Esta carpeta es ahora la copia de
trabajo activa del juego.**

## Dónde está todo AHORA

| Qué | Dónde | Estado |
|---|---|---|
| Juego — código fuente activo | `E:\Users\Alejandro\Opal\Tinted Showdown` (esta carpeta) | ✓ al día con `origin/main` (`2993f20`), working tree limpio |
| Juego — remote GitHub | `git@github.com:mandrix/TintedShowdown.git` | ✓ (considerar actualizar a `alejandroZumbado/TintedShowdown`) |
| Juego — build WebGL local | `E:\Users\Alejandro\Opal\Builds\Tinten Showdown` | sin verificar en esta sesión |
| Juego — jugable online | https://alejandrozumbado.github.io/tinted-showdown-build/ | sin verificar en esta sesión |
| Juego — carpeta vieja (rota) | `C:\Users\User\Desktop\juegos unity\Tinted Showdown` | `.git` y `Assets` vacíos — no usar, o re-clonar si se quiere recuperar ahí también |
| QuickLobby — código original | `C:\Users\User\Desktop\RelayLobbyKit` | ✓ intacto, con 3 fixes sin commitear |
| QuickLobby — respaldo nuevo | `E:\Users\Alejandro\Opal\QuickLobby-NetcodeRelayKit` | ✓ copia fiel creada en esta sesión (2026-08-18), mismos fixes sin commitear |
| QuickLobby — remote GitHub | https://github.com/alejandroZumbado/QuickLobby-NetcodeRelayKit (privado) | ✓ |
| QuickLobby — proyecto de prueba | `C:\Users\User\Desktop\QuickLobbyTestProject` | ✓ intacto, solo en C: |

## Un stash quedó pendiente en esta carpeta

Antes del `git pull`, esta carpeta tenía 3 archivos modificados sin commitear
(`Packages/manifest.json`, `ProjectSettings/ProjectSettings.asset`,
`ProjectSettings/ProjectVersion.txt`) — todo indica que es churn automático de haber
abierto el proyecto viejo con Unity 6000.3.13f1 en vez de la versión original
6000.1.7f1 (bump de versiones de paquetes + versión de Editor). Se guardó en
`git stash` en vez de descartarlo, por las dudas:

```
git stash list
# stash@{0}: On main: editor-version-churn-before-restore-pull
```

Si al abrir el proyecto con Unity 6000.3.13f1 estos mismos archivos vuelven a
modificarse solos, es seguro ignorar el stash (era ruido, no trabajo real) y
eventualmente `git stash drop`.

## Próximos pasos sugeridos

1. Abrir esta carpeta en Unity 6000.3.13f1 y confirmar que compila sin errores.
2. Ver `docs/PROJECT_OVERVIEW.md` para la sección "Pendiente / sin confirmar" del juego.
3. Ver `docs/QUICKLOBBY_PACKAGE.md` — primer paso ahí: confirmar el commit de los 3 fixes de code review.
4. Decidir si actualizar el remote `origin` del juego a la cuenta `alejandroZumbado`.
5. Opcional: investigar/limpiar la carpeta rota en `C:\...\juegos unity\Tinted Showdown`
   (re-clonar ahí también, o borrarla, una vez confirmado que esta copia en E: es la buena).
