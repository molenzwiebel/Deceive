using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Deceive;

internal class ConfigProxy
{
    private const string ConfigUrl = "https://clientconfig.rpg.riotgames.com";
    private const string GeoPasUrl = "https://riot-geo.pas.si.riotgames.com/pas/v1/service/chat";
    public const string LocalhostDomain = "deceive-localhost.molenzwiebel.xyz";

    /**
     * Starts a new client configuration proxy at a random port. The proxy will modify any responses
     * to point the chat servers to our local setup. This function returns the random port that the HTTP
     * server is listening on.
     */
    internal ConfigProxy(int chatPort)
    {
        ChatPort = chatPort;

        // Listen on a random free loopback port.
        Listener = new TcpListener(IPAddress.Loopback, 0);
        Listener.Start();
        ConfigPort = ((IPEndPoint)Listener.LocalEndpoint).Port;

        Task.Run(AcceptLoopAsync);
    }

    private TcpListener Listener { get; }

    private HttpClient Client { get; } = new();
    internal int ConfigPort { get; }
    private int ChatPort { get; }

    internal event EventHandler<ChatServerEventArgs>? PatchedChatServer;

    /**
     * Proxies any request made to this web server to the clientconfig service. Rewrites the response
     * to have any chat servers point to localhost at the specified port.
     */
    private async Task<(int StatusCode, string? ReasonPhrase, string Content)> ProxyAndRewriteResponseAsync(HttpRequest request)
    {
        var url = ConfigUrl + request.Target;
        Trace.WriteLine("Received client proxy request to URL: " + url);

        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        // Cloudflare bitches at us without a user agent.
        message.Headers.TryAddWithoutValidation("User-Agent", request.Header("user-agent"));

        // Add authorization headers for player config.
        if (request.Header("x-riot-entitlements-jwt") is not null)
            message.Headers.TryAddWithoutValidation("X-Riot-Entitlements-JWT", request.Header("x-riot-entitlements-jwt"));

        if (request.Header("authorization") is not null)
            message.Headers.TryAddWithoutValidation("Authorization", request.Header("authorization"));

        var result = await Client.SendAsync(message);
        Trace.WriteLine("Received response from clientconfig service with status code: " + result.StatusCode);
        var content = await result.Content.ReadAsStringAsync();
        var modifiedContent = content;
        TrafficLog.Write("ORIGINAL CLIENTCONFIG: ", content);

        // sometimes riot yields an internal error with content that is definitely
        // not json. we can just forward it to the riot client, which will retry
        // the request until it succeeds
        if (!result.IsSuccessStatusCode)
            goto RESPOND;

        try
        {
            var configObject = JsonNode.Parse(content);

            string? riotChatHost = null;
            var riotChatPort = 0;

            // Set fallback host to localhost.
            if (configObject?["chat.host"] is not null)
            {
                // Save fallback host
                riotChatHost = configObject["chat.host"]!.GetValue<string>();
                configObject["chat.host"] = LocalhostDomain;
            }

            // Set chat port.
            if (configObject?["chat.port"] is not null)
            {
                riotChatPort = configObject["chat.port"]!.GetValue<int>();
                configObject["chat.port"] = ChatPort;
            }

            // Set chat.affinities (a dictionary) to all localhost.
            if (configObject?["chat.affinities"] is not null)
            {
                var affinities = configObject["chat.affinities"];
                if (configObject["chat.affinity.enabled"]?.GetValue<bool>() ?? false)
                {
                    var pasRequest = new HttpRequestMessage(HttpMethod.Get, GeoPasUrl);
                    pasRequest.Headers.TryAddWithoutValidation("Authorization", request.Header("authorization"));

                    try
                    {
                        var pasJwt = await (await Client.SendAsync(pasRequest)).Content.ReadAsStringAsync();
                        var pasJwtContent = pasJwt.Split('.')[1];
                        var validBase64 = pasJwtContent.PadRight((pasJwtContent.Length / 4 * 4) + (pasJwtContent.Length % 4 == 0 ? 0 : 4), '=');
                        var pasJwtString = Encoding.UTF8.GetString(Convert.FromBase64String(validBase64));
                        var pasJwtJson = JsonNode.Parse(pasJwtString);
                        var affinity = pasJwtJson?["affinity"]?.GetValue<string>();

                        // replace fallback host with host by player affinity
                        if (affinity is not null)
                        {
                            riotChatHost = affinities?[affinity]?.GetValue<string>();
                            Trace.WriteLine($"AFFINITY: {affinity} -> {riotChatHost}");
                        }
                    }
                    catch (Exception e)
                    {
                        Trace.WriteLine("Error getting player affinity token, using default chat server.");
                        Trace.WriteLine(e);
                    }
                }

                affinities?.AsObject().Select(pair => pair.Key).ToList().ForEach(s => affinities[s] = LocalhostDomain);
            }

            modifiedContent = configObject?.ToJsonString() ?? content;
            TrafficLog.Write("MODIFIED CLIENTCONFIG: ", modifiedContent);

            if (riotChatHost is not null && riotChatPort != 0)
                PatchedChatServer?.Invoke(this, new ChatServerEventArgs { ChatHost = riotChatHost, ChatPort = riotChatPort });
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);

            // Show a message instead of failing silently.
            MessageBox.Show(
                "Deceive was unable to rewrite a League of Legends configuration file. This normally happens because Riot changed something on their end. " +
                "Please check if there's a new version of Deceive available, or contact the creator through GitHub (https://github.com/molenzwiebel/Deceive) or Discord if there's not.\n\n" +
                ex,
                StartupHandler.DeceiveTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1
            );

            Environment.Exit(0);
        }

RESPOND:
        return ((int)result.StatusCode, result.ReasonPhrase, modifiedContent);
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            try
            {
                var client = await Listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }
    }

    // Minimal HTTP/1.1 handling: one GET request per connection, then close.
    private async Task HandleClientAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var request = await HttpRequest.ReadAsync(stream);
            if (request is null)
                return;

            int statusCode;
            string? reasonPhrase;
            string content;
            if (request.Method == "GET")
            {
                try
                {
                    (statusCode, reasonPhrase, content) = await ProxyAndRewriteResponseAsync(request);
                }
                catch (Exception ex)
                {
                    // e.g. clientconfig unreachable; the Riot Client will retry.
                    Trace.WriteLine(ex);
                    (statusCode, reasonPhrase, content) = (502, "Bad Gateway", string.Empty);
                }
            }
            else
            {
                (statusCode, reasonPhrase, content) = (405, "Method Not Allowed", string.Empty);
            }

            var body = Encoding.UTF8.GetBytes(content);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} {reasonPhrase ?? "Unknown"}\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");

            await stream.WriteAsync(head);
            await stream.WriteAsync(body);
            await stream.FlushAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
        }
    }

    internal sealed class HttpRequest
    {
        private const int MaxHeaderBytes = 64 * 1024;

        private HttpRequest(string method, string target, Dictionary<string, string> headers)
        {
            Method = method;
            Target = target;
            Headers = headers;
        }

        internal string Method { get; }
        internal string Target { get; }
        private Dictionary<string, string> Headers { get; }

        internal string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

        internal static async Task<HttpRequest?> ReadAsync(Stream stream)
        {
            // Read until the end of the header block. GET requests have no body.
            var buffer = ArrayPool<byte>.Shared.Rent(MaxHeaderBytes);
            string head;
            try
            {
                var length = 0;
                int headerEnd;
                while ((headerEnd = IndexOfHeaderEnd(buffer, length)) < 0)
                {
                    if (length == MaxHeaderBytes)
                        return null;
                    var read = await stream.ReadAsync(buffer.AsMemory(length, MaxHeaderBytes - length));
                    if (read == 0)
                        return null;
                    length += read;
                }

                head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var lines = head.Split("\r\n");
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
                return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                    headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            return new HttpRequest(requestLine[0], requestLine[1], headers);
        }

        private static int IndexOfHeaderEnd(byte[] buffer, int length) =>
            buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
    }

    internal class ChatServerEventArgs : EventArgs
    {
        internal string? ChatHost { get; set; }
        internal int ChatPort { get; set; }
    }
}
