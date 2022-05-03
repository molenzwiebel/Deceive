using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;
using EmbedIO;
using EmbedIO.Actions;

namespace Deceive;

internal class ConfigProxy
{
    private const string ConfigUrl = "https://clientconfig.rpg.riotgames.com";
    private const string GeoPasUrl = "https://riot-geo.pas.si.riotgames.com/pas/v1/service/chat";

    /**
     * Starts a new client configuration proxy at a random port. The proxy will modify any responses
     * to point the chat servers to our local setup. This function returns the random port that the HTTP
     * server is listening on.
     */
    internal ConfigProxy(int chatPort)
    {
        ChatPort = chatPort;

        // Find a free port.
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();

        ConfigPort = port;

        // Start a web server that sends everything to ProxyAndRewriteResponse
        var server = new WebServer(o => o
                .WithUrlPrefix("http://127.0.0.1:" + port)
                .WithMode(HttpListenerMode.EmbedIO))
            .WithModule(new ActionModule("/", HttpVerbs.Get, ProxyAndRewriteResponseAsync));

        // For anything older than Windows 10, use TLS 1.2 and disable certificate validation.
        // Needs entries uncommented in app.manifest to detect the OS version properly.
        if (Environment.OSVersion.Version.Major < 10)
        {
            Trace.WriteLine("Found OS older than Windows 10: Use TLS 1.2 and disable certificate validation.");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = (_, _, _, _) => true;
        }

        // Catch exceptions in ProxyAndRewriteResponse
        server.OnHttpException += (_, exception) =>
        {
            Trace.WriteLine(exception);
            return Task.CompletedTask;
        };
        server.OnUnhandledException += (_, exception) =>
        {
            Trace.WriteLine(exception);
            return Task.CompletedTask;
        };

        Task.Run(() => server.RunAsync());
    }

    private HttpClient Client { get; } = new();
    internal int ConfigPort { get; }
    private int ChatPort { get; }

    internal event EventHandler<ChatServerEventArgs>? PatchedChatServer;

    /**
     * Proxies any request made to this web server to the clientconfig service. Rewrites the response
     * to have any chat servers point to localhost at the specified port.
     */
    private async Task ProxyAndRewriteResponseAsync(IHttpContext ctx)
    {
        var url = ConfigUrl + ctx.Request.RawUrl;
        Trace.WriteLine("Received client proxy request to URL: " + url);

        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        // Cloudflare bitches at us without a user agent.
        message.Headers.TryAddWithoutValidation("User-Agent", ctx.Request.Headers["user-agent"]);

        // Add authorization headers for player config.
        if (ctx.Request.Headers["x-riot-entitlements-jwt"] is not null)
            message.Headers.TryAddWithoutValidation("X-Riot-Entitlements-JWT", ctx.Request.Headers["x-riot-entitlements-jwt"]);

        if (ctx.Request.Headers["authorization"] is not null)
            message.Headers.TryAddWithoutValidation("Authorization", ctx.Request.Headers["authorization"]);

        var result = await Client.SendAsync(message);
        Trace.WriteLine("Received response from clientconfig service with status code: " + result.StatusCode);
        var content = await result.Content.ReadAsStringAsync();
        var modifiedContent = content;
        Trace.WriteLine("ORIGINAL CLIENTCONFIG: " + content);

        // sometimes riot yields an internal error with content that is definitely
        // not json. we can just forward it to the riot client, which will retry
        // the request until it succeeds
        if (!result.IsSuccessStatusCode)
            goto RESPOND;

        try
        {
            var configObject = JsonSerializer.Deserialize<JsonNode>(content);

            string? riotChatHost = null;
            var riotChatPort = 0;

            // Set fallback host to localhost.
            if (configObject?["chat.host"] is not null)
            {
                // Save fallback host
                riotChatHost = configObject["chat.host"]!.GetValue<string>();
                configObject["chat.host"] = "127.0.0.1";
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
                    pasRequest.Headers.TryAddWithoutValidation("Authorization", ctx.Request.Headers["authorization"]);

                    try
                    {
                        var pasJwt = await (await Client.SendAsync(pasRequest)).Content.ReadAsStringAsync();
                        Trace.WriteLine("PAS JWT:" + pasJwt);
                        var pasJwtContent = pasJwt.Split('.')[1];
                        var validBase64 = pasJwtContent.PadRight((pasJwtContent.Length / 4 * 4) + (pasJwtContent.Length % 4 == 0 ? 0 : 4), '=');
                        var pasJwtString = Encoding.UTF8.GetString(Convert.FromBase64String(validBase64));
                        var pasJwtJson = JsonSerializer.Deserialize<JsonNode>(pasJwtString);
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

                affinities?.AsObject().Select(pair => pair.Key).ToList().ForEach(s => affinities[s] = "127.0.0.1");
            }

            // Allow an invalid cert.
            if (configObject?["chat.allow_bad_cert.enabled"] is not null)
                configObject["chat.allow_bad_cert.enabled"] = true;

            modifiedContent = JsonSerializer.Serialize(configObject);
            Trace.WriteLine("MODIFIED CLIENTCONFIG: " + modifiedContent);

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

            Application.Exit();
        }

        // Using the builtin EmbedIO methods for sending the response adds some garbage in the front of it.
        // This seems to do the trick.
RESPOND:
        var responseBytes = Encoding.UTF8.GetBytes(modifiedContent);

        ctx.Response.StatusCode = (int)result.StatusCode;
        ctx.Response.SendChunked = false;
        ctx.Response.ContentLength64 = responseBytes.Length;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.OutputStream.WriteAsync(responseBytes);
        ctx.Response.OutputStream.Close();
    }

    internal class ChatServerEventArgs : EventArgs
    {
        internal string? ChatHost { get; init; }
        internal int ChatPort { get; init; }
    }
}
