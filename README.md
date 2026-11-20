# Claude City: playing Cities: Skylines II through an MCP server

This folder lets an AI agent play **Cities: Skylines II** (Xbox / PC Game Pass build, v1.6.4)
without looking at screenshots. A code mod inside the game exposes the city as a local HTTP
API, and a small MCP server turns that API into tools.

```
Claude ──MCP (stdio)──> mcp/server.mjs ──HTTP 127.0.0.1:8787──> ClaudeCityBridge mod (in game)
                                                                  ├─ core DLL   (Mods/)      HTTP server, ECS systems, agent tool
                                                                  └─ API plugin (ModsData/)  endpoints, hot-reloadable
```

## How the city gets built

Building is split over agents: a conductor (the main Claude session), a read-only advisory board that writes
goal-level work items per district, long-lived district builders and a troubleshooter that work through them
continuously, and a first-come-first-served build lock in the mod (the "busy tone") so only one of them changes
the map at a time. See [docs/ORCHESTRATION.md](docs/ORCHESTRATION.md); builders follow
[docs/council/builder_handbook.md](docs/council/builder_handbook.md).

## Pieces

| Path | What |
| --- | --- |
| `mod/ClaudeCityBridge/` | Core mod (`IMod`). Starts an `HttpListener` on `127.0.0.1:8787`, marshals every request to the game's main thread (`BridgeSystem`, update phase `MainLoop`), and owns `AgentToolSystem`. |
| `mod/ClaudeCityBridge/AgentToolSystem.cs` | A headless tool (`ObjectToolBaseSystem` subclass) that builds through the game's own tool pipeline: it writes `CreationDefinition` + `NetCourse` / `Zoning` / object definitions exactly like the vanilla tools, waits for the game to generate and validate the temporary preview, reads cost and errors, then sets `applyMode = Apply` (or `Clear` for dry runs and errors). Money, unlocks and validation therefore work like normal play. |
| `mod/ClaudeCityApi/` | All endpoints (dashboard, text map, roads, zoning, buildings, budget, taxes, saving, UI unblocking). Compiled to a separate DLL that the core loads from bytes; `POST /reload` swaps in a new build without restarting the game. |
| `mcp/server.mjs`, `mcp/tools.mjs` | Dependency-free MCP server (stdio, JSON-RPC 2.0) with 22 `cs2_*` tools mapped onto the HTTP API. Registered in `.mcp.json`. |
| `tools/ildump/` | Small IL/metadata dumper (System.Reflection.Metadata) used to study `Game.dll`, since no decompiler was available. `d.sh members|il|find|types <pattern>`. |
| `tools/cc.py` | Python client for the same API (`python -I cc.py GET city`, `python -I cc.py POST road '{...}'`). |
| `tools/phase1.py` | Reproducible start of Claude City: new game on Corral Riches, first grid, zoning, water, power, sewage, industry. |
| `tools/autopilot.py` | Keeps the city running between planning rounds: resumes after popups, logs the dashboard to `logs/city.jsonl`, saves periodically, flags shortages. |
| `tools/start-game.sh` | Launches the game and clicks through the "press any key" engagement screen (the game waits for real input there before loading mods). |
| `tools/input.ps1`, `tools/screenshot.ps1` | Fallback only: real mouse/keyboard input and a screen grab, for first-run screens the bridge cannot reach. |

## Build and deploy

Needs Visual Studio Build Tools (Roslyn `csc.exe`); no .NET SDK required.

```bash
cd mod
./build.sh all        # core -> %LocalLow%/Colossal Order/Cities Skylines II/Mods/ClaudeCityBridge/
                      # api  -> %LocalLow%/Colossal Order/Cities Skylines II/ModsData/ClaudeCityBridge/
./build.sh api && curl -s -X POST http://127.0.0.1:8787/reload -d '{}'   # hot-swap endpoints
```

The core DLL is loaded by the game at startup (local mods in `Mods/` load without a playset);
changing it needs a game restart.

## Run

```bash
tools/start-game.sh                 # start game, pass engagement screen, wait for the bridge
python -I tools/phase1.py           # start/extend Claude City
python -I tools/autopilot.py --minutes 20
```

First launch only: the game shows the Paradox user agreement and privacy notice (OK buttons)
and a welcome dialog; `POST /ui {"action":"notutorials"}` turns tutorials off (the intro tutorial
otherwise keeps the whole toolbar locked).

## HTTP API (all JSON)

| Endpoint | Purpose |
| --- | --- |
| `GET /ping`, `GET /status` | liveness, mode (MainMenu/Game), city name, date, speed, population, money |
| `GET /maps`, `POST /newgame {map, cityName}` | list maps, start a new city (normal rules) |
| `GET /city` | dashboard: population, money, income/expenses, demand, electricity, water, sewage, milestone, tax |
| `POST /speed {speed}`, `POST /wait {days, speed, maxSeconds}` | run time; both close popups that pause the game |
| `GET /map?x&z&size&cell` | ASCII map: water, steep terrain, owned tiles, roads, zones, buildings |
| `GET /areainfo`, `POST /terrain {points}` | playable area, owned tiles, outside connections; height / water depth |
| `GET /nets?x&z&radius&filter` | road/net segments with ids, endpoints, node degree |
| `GET /prefabs?category&filter&unlockedOnly` | roads, services, zones, nets with lock state, cost, lot size |
| `POST /road {prefab, points, snap, dryRun}` | build a net along a polyline; snaps to and splits existing roads |
| `POST /zone {zone, mode: rect/road, points/edges, depth, side}` | marquee zoning |
| `POST /building {prefab, edge, t, side | x, z, angle, dryRun}` | place a service building facing a road |
| `GET /buildings`, `GET /problems`, `GET /milestones` | buildings, notification icons by type, milestone list |
| `POST /taxes`, `POST /budget` | read/set tax rates and service budgets |
| `POST /save {name}` | save like the Save panel (with a generated preview) |
| `POST /ui {action}` | `status`, `resume`, `dismiss`, `confirm`, `notutorials` |
| `POST /reload` | reload the API plugin DLL |

Coordinates are world metres: x east, z north, y height. Entity ids are `index:version`.
