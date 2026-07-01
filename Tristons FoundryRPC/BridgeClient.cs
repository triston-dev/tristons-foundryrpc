// =============================================================================
//  Triston's FoundryRPC  —  BridgeClient.cs
//  Speaks the Foundry MCP bridge's control-channel protocol: a newline-delimited
//  JSON ("JSON-lines") request/response over a plain TCP socket, default
//  127.0.0.1:31414. One short-lived connection per call — simple and robust at a
//  15s cadence.
//
//  Wire format (verified live against Triston's Bridge Fork backend):
//    ->  {"id":"<id>","method":"call_tool","params":{"name":"get-world-info","args":{}}}\n
//    <-  {"id":"<id>","result":{"content":[{"type":"text","text":"<world-json-string>"}]}}\n
//    error tool:   {"id":..,"result":{"content":[{"text":"Error: ..."}],"isError":true}}
//    bad method:   {"id":..,"error":{"message":"Unknown method"}}
//
//  The world object is itself a JSON *string* inside content[0].text — callers
//  parse that with WorldStatus.FromWorldInfoJson.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TristonsFoundryRPC;

/// <summary>Outcome of a single bridge call.</summary>
public readonly struct BridgeCallResult
{
    /// <summary>The tool succeeded and <see cref="Payload"/> holds its text.</summary>
    public bool Ok { get; init; }

    /// <summary>Unwrapped tool text (for get-world-info: the world JSON string).</summary>
    public string? Payload { get; init; }

    /// <summary>Human-readable failure reason (tool error, protocol error, etc.).</summary>
    public string? Error { get; init; }

    /// <summary>
    /// False when the socket could not be reached at all (backend down / refused /
    /// timed out) — distinct from a reachable backend returning a tool error.
    /// </summary>
    public bool Reachable { get; init; }

    public static BridgeCallResult Unreachable(string reason) =>
        new() { Ok = false, Reachable = false, Error = reason };

    public static BridgeCallResult Failed(string reason) =>
        new() { Ok = false, Reachable = true, Error = reason };

    public static BridgeCallResult Success(string payload) =>
        new() { Ok = true, Reachable = true, Payload = payload };
}

/// <summary>
/// Low-level client for the bridge control channel. Reads host/port live from
/// <see cref="Config"/> so changes made via the tray menu take effect on the
/// next poll. Never throws to callers — failures come back as
/// <see cref="BridgeCallResult"/>.
/// </summary>
public sealed class BridgeClient
{
    private readonly Config _config;
    private readonly Logger _log;
    private int _nextId;

    // Connect fast (local socket); allow a little longer for the whole exchange.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(6);

    public BridgeClient(Config config, Logger log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>Call get-world-info. The success payload is the world JSON string.</summary>
    public Task<BridgeCallResult> GetWorldInfoAsync(CancellationToken ct) =>
        CallToolAsync("get-world-info", ct);

    /// <summary>Liveness check — returns true if the backend answered ping.ok.</summary>
    public async Task<bool> PingAsync(CancellationToken ct)
    {
        var id = NextId();
        var req = $"{{\"id\":\"{id}\",\"method\":\"ping\"}}";
        try
        {
            var line = await SendLineAsync(req, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("result", out var r)
                   && r.TryGetProperty("ok", out var ok)
                   && ok.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Invoke a bridge tool by name with empty args and unwrap its text.</summary>
    public async Task<BridgeCallResult> CallToolAsync(string toolName, CancellationToken ct)
    {
        var id = NextId();
        // args:{} — get-world-info takes no arguments.
        var req = $"{{\"id\":\"{id}\",\"method\":\"call_tool\",\"params\":{{\"name\":\"{toolName}\",\"args\":{{}}}}}}";

        string line;
        try
        {
            line = await SendLineAsync(req, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // App is shutting down — treat as unreachable without noise.
            return BridgeCallResult.Unreachable("cancelled");
        }
        catch (Exception ex)
        {
            // Connection refused / timeout / reset => backend not up.
            _log.Debug($"Bridge unreachable at {_config.BridgeHost}:{_config.BridgePort} — {ex.GetType().Name}: {ex.Message}");
            return BridgeCallResult.Unreachable($"{ex.GetType().Name}: {ex.Message}");
        }

        return ParseEnvelope(line);
    }

    // -------------------------------------------------------------------------
    //  Protocol plumbing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Open a socket, write one JSON line, read exactly one newline-terminated
    /// line back, and return it (trimmed). The server keeps the connection open
    /// after responding, so we stop at the first '\n' rather than at EOF.
    /// </summary>
    private async Task<string> SendLineAsync(string json, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CallTimeout);
        var linked = timeoutCts.Token;

        using var client = new TcpClient();

        // Connect with its own tighter budget.
        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(linked))
        {
            connectCts.CancelAfter(ConnectTimeout);
            await client.ConnectAsync(_config.BridgeHost, _config.BridgePort, connectCts.Token)
                        .ConfigureAwait(false);
        }

        client.NoDelay = true;
        using var stream = client.GetStream();

        var payload = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(payload, linked).ConfigureAwait(false);
        await stream.FlushAsync(linked).ConfigureAwait(false);

        var sb = new StringBuilder(512);
        var buffer = new byte[4096];
        while (true)
        {
            int n = await stream.ReadAsync(buffer, linked).ConfigureAwait(false);
            if (n <= 0)
                break; // peer closed before sending a full line

            sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
            int nl = IndexOfNewline(sb);
            if (nl >= 0)
                return sb.ToString(0, nl).Trim();
        }

        throw new IOException("Bridge closed the connection without returning a line.");
    }

    private static int IndexOfNewline(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++)
            if (sb[i] == '\n')
                return i;
        return -1;
    }

    /// <summary>Turn a raw response line into a <see cref="BridgeCallResult"/>.</summary>
    private BridgeCallResult ParseEnvelope(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            // Top-level protocol error (e.g. unknown method).
            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "unknown protocol error";
                return BridgeCallResult.Failed(msg ?? "unknown protocol error");
            }

            if (!root.TryGetProperty("result", out var result))
                return BridgeCallResult.Failed("response had neither result nor error");

            // Tool-style result: { content: [ { text } ], isError? }
            if (result.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.Array &&
                content.GetArrayLength() > 0)
            {
                var first = content[0];
                var text = first.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

                bool isError = result.TryGetProperty("isError", out var ie) &&
                               ie.ValueKind == JsonValueKind.True;

                return isError ? BridgeCallResult.Failed(text) : BridgeCallResult.Success(text);
            }

            // Non-tool result (e.g. ping) — hand back the raw result JSON.
            return BridgeCallResult.Success(result.GetRawText());
        }
        catch (JsonException ex)
        {
            return BridgeCallResult.Failed($"malformed JSON from bridge: {ex.Message}");
        }
    }

    private string NextId() => Interlocked.Increment(ref _nextId).ToString();
}
