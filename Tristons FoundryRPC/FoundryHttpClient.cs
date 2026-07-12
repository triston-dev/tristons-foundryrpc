// =============================================================================
//  Triston's FoundryRPC  —  FoundryHttpClient.cs
//  Bridge-free world detection over plain HTTP, two methods per server:
//
//  A) GET <server>/api/status — Foundry v13's built-in unauthenticated status
//     endpoint. Works on directly-exposed servers (e.g. http://localhost:30000).
//     Gives the world *slug* only.
//
//  B) Socket.IO join-data — for hosts whose proxy swallows /api/* (verified
//     live against Sqyre 2026-07): Foundry hands any visitor an anonymous
//     session cookie on GET /game, and a Socket.IO connection made with that
//     cookie may emit "getJoinData", which returns the world's PRETTY TITLE,
//     id, system, core version, and the list of active users — exactly what
//     the login screen shows. Implemented as raw Engine.IO long-polling
//     (a handful of HTTP requests), no websocket or client library needed.
//
//  Method A is tried first (one cheap GET); when a server answers /api/status
//  with something that is not Foundry's shape, the server is remembered as
//  method-B for subsequent polls. All configured servers are polled in
//  parallel; the first (config-order) active world wins.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TristonsFoundryRPC;

public sealed class FoundryHttpClient : IDisposable
{
    private readonly Config _config;
    private readonly Logger _log;
    private readonly HttpClient _http;

    private enum Method { Unknown, ApiStatus, SocketJoinData }

    // Per-server memory: which method works, and the anonymous session cookie
    // for method B (refreshed automatically when Foundry stops honoring it).
    private readonly ConcurrentDictionary<string, Method> _methods = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _cookies = new(StringComparer.OrdinalIgnoreCase);

    private bool _warnedNoServers;

    public FoundryHttpClient(Config config, Logger log)
    {
        _config = config;
        _log = log;
        _http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, // we read the session cookie off Foundry's /game redirect
            UseCookies = false,        // cookies are managed per-server by hand
        })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "TristonsFoundryRPC/2.0 (+https://github.com/triston-dev/tristons-foundryrpc)");
    }

    /// <summary>
    /// Poll every configured server; return the first active world (config order)
    /// or <see cref="WorldStatus.Idle"/>. Never throws.
    /// </summary>
    public async Task<WorldStatus> GetActiveWorldAsync(CancellationToken ct)
    {
        var servers = NormalizedServers();
        if (servers.Count == 0)
        {
            if (!_warnedNoServers)
            {
                _warnedNoServers = true;
                _log.Info("No Foundry server URLs configured — tray menu → " +
                          "\"Configure Foundry Server URLs…\" (presence stays idle until then).");
            }
            return WorldStatus.Idle;
        }
        _warnedNoServers = false;

        var polls = servers.Select(s => PollServerAsync(s, ct)).ToArray();
        var results = await Task.WhenAll(polls).ConfigureAwait(false);
        return results.FirstOrDefault(r => r.Active) ?? WorldStatus.Idle;
    }

    // -------------------------------------------------------------------------
    //  Per-server poll
    // -------------------------------------------------------------------------

    private async Task<WorldStatus> PollServerAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            var method = _methods.GetValueOrDefault(baseUrl, Method.Unknown);

            if (method is Method.Unknown or Method.ApiStatus)
            {
                var viaApi = await TryApiStatusAsync(baseUrl, ct).ConfigureAwait(false);
                if (viaApi is not null)
                {
                    _methods[baseUrl] = Method.ApiStatus;
                    return viaApi;
                }
                // /api/status is not Foundry's on this host — remember and fall through.
                if (method == Method.Unknown)
                    _log.Info($"{baseUrl}: /api/status not available; using socket join-data method.");
                _methods[baseUrl] = Method.SocketJoinData;
            }

            return await TrySocketJoinDataAsync(baseUrl, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return WorldStatus.Idle; // app shutting down
        }
        catch (Exception ex)
        {
            // Refused / timeout / DNS / malformed payload — this server is idle.
            _log.Debug($"{baseUrl}: {ex.GetType().Name}: {ex.Message}");
            return WorldStatus.Idle;
        }
    }

    /// <summary>Method A. Returns null when the endpoint isn't Foundry's (proxy 404 etc.).</summary>
    private async Task<WorldStatus?> TryApiStatusAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync($"{baseUrl}/api/status", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            // Foundry's shape always carries "active"; hosting-provider APIs answer
            // something else (e.g. {"success":false,...}) — treat those as "not Foundry".
            if (!doc.RootElement.TryGetProperty("active", out _))
                return null;

            return WorldStatus.FromApiStatusJson(json, baseUrl);
        }
        catch (JsonException)
        {
            return null; // HTML or garbage — not Foundry's endpoint
        }
        catch (HttpRequestException)
        {
            // Server unreachable — genuine idle, not a method mismatch. Don't
            // switch methods on this; rethrow to be handled as idle upstream.
            throw;
        }
    }

    // -------------------------------------------------------------------------
    //  Method B: Engine.IO long-polling → getJoinData
    //  Wire choreography (verified live against Foundry 13.351 behind Sqyre):
    //    1. GET /game                    → 302 + Set-Cookie: session=<id>
    //    2. GET /socket.io/?EIO=4&transport=polling&session=<id>   → 0{"sid":...}
    //    3. POST "40"                    → "ok"                (namespace connect)
    //    4. GET                          → 40{...}             (connect ack)
    //    5. POST 420["getJoinData"]      → "ok"                (emit w/ ack id 0)
    //    6. GET (repeat, answering "2" pings with "3")          → 430[{...}]
    //  The 430 payload: { release, world{id,title,system,coreVersion,...},
    //                     users[], activeUsers[], ... }
    // -------------------------------------------------------------------------

    private async Task<WorldStatus> TrySocketJoinDataAsync(string baseUrl, CancellationToken ct)
    {
        var cookie = await GetSessionCookieAsync(baseUrl, ct).ConfigureAwait(false);
        if (cookie is null)
            return WorldStatus.Idle;

        var sessionId = cookie.Substring(cookie.IndexOf('=') + 1);
        var ep = $"{baseUrl}/socket.io/?EIO=4&transport=polling&session={sessionId}";

        async Task<string> Get(string sid) =>
            await SendAsync(HttpMethod.Get, ep + (sid.Length > 0 ? $"&sid={sid}" : ""), cookie, null, ct)
                .ConfigureAwait(false);
        async Task<string> Post(string sid, string body) =>
            await SendAsync(HttpMethod.Post, $"{ep}&sid={sid}", cookie, body, ct).ConfigureAwait(false);

        // 2. handshake
        var hs = await Get("").ConfigureAwait(false);
        if (!hs.StartsWith("0", StringComparison.Ordinal))
        {
            // Session cookie no longer honored (server restarted?) — drop it so the
            // next poll fetches a fresh one.
            _cookies.TryRemove(baseUrl, out _);
            _log.Debug($"{baseUrl}: unexpected handshake '{Truncate(hs, 60)}'; session cookie reset.");
            return WorldStatus.Idle;
        }
        string sid;
        using (var doc = JsonDocument.Parse(hs.Substring(1)))
            sid = doc.RootElement.GetProperty("sid").GetString()!;

        // 3-4. connect to the default namespace
        await Post(sid, "40").ConfigureAwait(false);
        await Get(sid).ConfigureAwait(false); // drain the connect ack + session event

        // 5. emit getJoinData with ack id 0
        await Post(sid, "420[\"getJoinData\"]").ConfigureAwait(false);

        // 6. read until the ack (packets may arrive concatenated, 0x1E-separated)
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var payload = await Get(sid).ConfigureAwait(false);
            // Engine.IO batches packets with the 0x1E record separator.
            foreach (var packet in payload.Split((char)30))
            {
                if (packet == "2") { await Post(sid, "3").ConfigureAwait(false); continue; } // ping→pong
                if (packet.StartsWith("43", StringComparison.Ordinal))
                {
                    _ = Post(sid, "1"); // polite disconnect, fire-and-forget
                    return ParseJoinData(packet, baseUrl);
                }
            }
        }

        _log.Debug($"{baseUrl}: getJoinData ack never arrived.");
        return WorldStatus.Idle;
    }

    /// <summary>Anonymous Foundry session cookie for a server, fetched via /game redirect.</summary>
    private async Task<string?> GetSessionCookieAsync(string baseUrl, CancellationToken ct)
    {
        if (_cookies.TryGetValue(baseUrl, out var cached))
            return cached;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/game");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            var session = setCookies
                .Select(c => c.Split(';')[0].Trim())
                .FirstOrDefault(c => c.StartsWith("session=", StringComparison.OrdinalIgnoreCase));
            if (session is not null)
            {
                _cookies[baseUrl] = session;
                _log.Debug($"{baseUrl}: obtained anonymous session cookie.");
                return session;
            }
        }

        _log.Debug($"{baseUrl}: /game returned no session cookie (HTTP {(int)resp.StatusCode}).");
        return null;
    }

    /// <summary>Turn a 430[...] ack packet into a WorldStatus.</summary>
    private static WorldStatus ParseJoinData(string packet, string baseUrl)
    {
        // "430" + JSON array; the payload is its first element.
        var json = packet.Substring(packet.IndexOf('['));
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement[0];

        if (!data.TryGetProperty("world", out var world) || world.ValueKind != JsonValueKind.Object)
            return WorldStatus.Idle; // no world loaded (setup screen)

        string? title = GetString(world, "title");
        string? id = GetString(world, "id");
        if (string.IsNullOrWhiteSpace(id))
            return WorldStatus.Idle;

        int active = 0;
        if (data.TryGetProperty("activeUsers", out var au) && au.ValueKind == JsonValueKind.Array)
            active = au.GetArrayLength();

        return new WorldStatus
        {
            Active = true,
            WorldId = id,
            Title = title,
            SystemId = GetString(world, "system"),
            SystemVersion = GetString(world, "systemVersion"),
            FoundryVersion = GetString(world, "coreVersion"),
            UsersActive = active,
            SourceUrl = baseUrl,
        };
    }

    // -------------------------------------------------------------------------
    //  Plumbing
    // -------------------------------------------------------------------------

    private async Task<string> SendAsync(HttpMethod method, string url, string cookie, string? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private List<string> NormalizedServers()
    {
        var list = new List<string>();
        foreach (var raw in _config.FoundryServers ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var url = raw.Trim().TrimEnd('/');
            // People paste the play URL — strip Foundry's route suffixes to the origin.
            foreach (var suffix in new[] { "/game", "/join", "/setup", "/auth", "/license" })
                if (url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    url = url.Substring(0, url.Length - suffix.Length);
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url; // bare hostnames get https by default
            list.Add(url);
        }
        return list;
    }

    private static string? GetString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public void Dispose() => _http.Dispose();
}
