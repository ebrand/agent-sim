#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AgentSimUnity
{
    /// <summary>
    /// Lightweight HTTP control endpoint for the running sim. Exposes a few config knobs over
    /// localhost so an external app (React, curl, etc.) can read and tweak sim behavior live.
    ///
    /// Endpoints:
    ///   GET  /health    → "ok"
    ///   GET  /config    → JSON snapshot of hot-swappable SimConfig fields
    ///   POST /config    → JSON body with any of the same fields → applied to the running sim
    ///
    /// All requests get permissive CORS so the Vite dev server on a different port can hit it.
    /// HttpListener runs on its own thread; tweaks queue onto a thread-safe channel and apply on
    /// the Unity main thread during Update().
    /// </summary>
    [RequireComponent(typeof(SimBootstrap))]
    public class ConfigServer : MonoBehaviour
    {
        [Tooltip("Localhost port to listen on. Pick anything not in use.")]
        public int Port = 8765;

        [Tooltip("Set false to disable the HTTP server (e.g. for builds where it's not wanted).")]
        public bool Enabled = true;

        private SimBootstrap _bootstrap = null!;
        private HttpListener? _listener;
        private Thread? _thread;
        private volatile bool _running;

        // Pending config changes parsed off the request thread; main thread applies them.
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();

        void Awake() => _bootstrap = GetComponent<SimBootstrap>();

        void OnEnable()
        {
            if (!Enabled) return;
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Start();
                _running = true;
                _thread = new Thread(ListenLoop) { IsBackground = true };
                _thread.Start();
                Debug.Log($"[ConfigServer] Listening on http://localhost:{Port}/");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConfigServer] Failed to start: {e.Message}");
                _listener = null;
            }
        }

        void OnDisable()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* swallow */ }
            _listener = null;
            _thread = null;
        }

        void Update()
        {
            while (_mainThreadActions.TryDequeue(out var act))
            {
                try { act(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // === HTTP request loop (background thread) ===

        private void ListenLoop()
        {
            while (_running && _listener is not null)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { break; }

                try { Handle(ctx); }
                catch (Exception e)
                {
                    try { WriteJson(ctx.Response, 500, $"{{\"error\":\"{Escape(e.Message)}\"}}"); }
                    catch { }
                }
                finally { try { ctx.Response.OutputStream.Close(); } catch { } }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var res = ctx.Response;
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath ?? "/";

            if (path == "/health" && ctx.Request.HttpMethod == "GET")
            {
                WriteJson(res, 200, "{\"status\":\"ok\"}");
                return;
            }

            if (path == "/config" && ctx.Request.HttpMethod == "GET")
            {
                // Snapshot off the main thread is safe for value-type reads; SimConfig is a
                // sealed record with property getters that don't mutate anything.
                var cfg = _bootstrap.Sim?.State.Config;
                if (cfg is null)
                {
                    WriteJson(res, 503, "{\"error\":\"sim not running\"}");
                    return;
                }
                WriteJson(res, 200, SerializeConfig(cfg));
                return;
            }

            if (path == "/config" && ctx.Request.HttpMethod == "POST")
            {
                string body;
                using (var r = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    body = r.ReadToEnd();

                // Queue the mutation onto the main thread — SimConfig is a Unity-scope object.
                _mainThreadActions.Enqueue(() => ApplyConfigPatch(body));
                WriteJson(res, 200, "{\"status\":\"queued\"}");
                return;
            }

            WriteJson(res, 404, "{\"error\":\"not found\"}");
        }

        private void ApplyConfigPatch(string body)
        {
            var cfg = _bootstrap.Sim?.State.Config;
            if (cfg is null) return;

            // Tiny ad-hoc JSON parser: just look for the keys we care about. Not great for
            // production but fine for a handful of booleans. If this grows, switch to
            // System.Text.Json (available in the Unity netstandard2.1 runtime).
            cfg.InstantConstruction = ReadBool(body, "instantConstruction", cfg.InstantConstruction);
            cfg.ImmigrationEnabled = ReadBool(body, "immigrationEnabled", cfg.ImmigrationEnabled);
            cfg.ServiceEmigrationEnabled = ReadBool(body, "serviceEmigrationEnabled", cfg.ServiceEmigrationEnabled);
            cfg.FoundingPhaseEnabled = ReadBool(body, "foundingPhaseEnabled", cfg.FoundingPhaseEnabled);
            cfg.GateBootstrapOnUtilities = ReadBool(body, "gateBootstrapOnUtilities", cfg.GateBootstrapOnUtilities);

            Debug.Log($"[ConfigServer] Patched config: {SerializeConfig(cfg)}");
        }

        // === Tiny JSON helpers ===

        private static string SerializeConfig(AgentSim.Core.Types.SimConfig cfg)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"instantConstruction\":{cfg.InstantConstruction.ToString().ToLowerInvariant()},");
            sb.Append($"\"immigrationEnabled\":{cfg.ImmigrationEnabled.ToString().ToLowerInvariant()},");
            sb.Append($"\"serviceEmigrationEnabled\":{cfg.ServiceEmigrationEnabled.ToString().ToLowerInvariant()},");
            sb.Append($"\"foundingPhaseEnabled\":{cfg.FoundingPhaseEnabled.ToString().ToLowerInvariant()},");
            sb.Append($"\"gateBootstrapOnUtilities\":{cfg.GateBootstrapOnUtilities.ToString().ToLowerInvariant()},");
            sb.Append($"\"seed\":{cfg.Seed},");
            sb.Append($"\"startingTreasury\":{cfg.StartingTreasury}");
            sb.Append('}');
            return sb.ToString();
        }

        private static bool ReadBool(string body, string key, bool fallback)
        {
            int i = body.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (i < 0) return fallback;
            int colon = body.IndexOf(':', i);
            if (colon < 0) return fallback;
            var rest = body.AsSpan(colon + 1).TrimStart();
            if (rest.StartsWith("true")) return true;
            if (rest.StartsWith("false")) return false;
            return fallback;
        }

        private static void WriteJson(HttpListenerResponse res, int status, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            res.StatusCode = status;
            res.ContentType = "application/json";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
