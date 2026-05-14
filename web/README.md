# AgentSim Tweaker

A small React app that connects to the Unity sim's local HTTP endpoint
and lets you flip hot-swappable config toggles live.

## Setup

```sh
cd web
npm install
npm run dev
```

This starts Vite on http://localhost:5173.

## Usage

1. Open the Unity project in `/unity/`, hit Play.
2. The `ConfigServer` MonoBehaviour on the SimBootstrap GameObject auto-starts
   an HTTP listener on `http://localhost:8765/`.
3. Open http://localhost:5173 in a browser → the panel loads the current config.
4. Toggle anything in "Hot-swappable toggles" → click Apply.
5. Read-only values (Seed, Starting Treasury) require restarting Play in Unity.

## Endpoints

- `GET  http://localhost:8765/health` → `{"status":"ok"}`
- `GET  http://localhost:8765/config` → current SimConfig JSON
- `POST http://localhost:8765/config` → patch with any subset of fields

Example with curl:

```sh
curl -X POST http://localhost:8765/config \
  -H 'Content-Type: application/json' \
  -d '{"instantConstruction": true}'
```

## Notes

- CORS is wide open (`Access-Control-Allow-Origin: *`) since this is dev-only.
- Server runs on a background thread; mutations queue onto the Unity main
  thread via a `ConcurrentQueue`.
- The JSON parser in `ConfigServer.cs` is intentionally ad-hoc — only the
  fields the UI cares about. Promote to `System.Text.Json` if it grows.
