using System;
using System.Net;
using System.IO;
using System.Threading.Tasks;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Launcher.Network
{
    public static class HaapiServer
    {
        private static HttpListener? _listener;
        private static Task? _listenTask;
        private static bool _isRunning;

        public static void Start(int port)
        {
            if (_isRunning) return;
            _isRunning = true;

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            Console.WriteLine($"[+] HAAPI HTTP Server listening on port {port}");

            _listenTask = Task.Run(async () =>
            {
                while (_isRunning && _listener != null)
                {
                    try
                    {
                        var ctx = await _listener.GetContextAsync();
                        _ = HandleHaapiRequestAsync(ctx);
                    }
                    catch (Exception ex)
                    {
                        if (!_isRunning) break;
                        Console.WriteLine($"[HAAPI Error] {ex.Message}");
                    }
                }
            });
        }

        public static void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
            _listener = null;
        }

        private static async Task HandleHaapiRequestAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;
            string path = req.Url?.AbsolutePath ?? "/";

            Console.WriteLine($"[HAAPI] {req.HttpMethod} {path}");

            // Read request body if present
            string body = "";
            if (req.HasEntityBody)
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                body = await reader.ReadToEndAsync();
                if (body.Length > 0) Console.WriteLine($"[HAAPI]  body: {body}");
            }

            try
            {
                string json = RouteHaapi(path, req.HttpMethod, body);
                byte[] buf = System.Text.Encoding.UTF8.GetBytes(json);
                resp.StatusCode = 200;
                resp.ContentType = "application/json; charset=utf-8";
                resp.ContentLength64 = buf.Length;
                
                // Allow the game to talk to us without CORS issues
                resp.AddHeader("Access-Control-Allow-Origin", "*");
                resp.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, apikey");
                await resp.OutputStream.WriteAsync(buf, 0, buf.Length);
            }
            catch (NotImplementedException nie)
            {
                Console.WriteLine($"[HAAPI]  !! Unhandled endpoint: {nie.Message}");
                resp.StatusCode = 404;
                byte[] buf = System.Text.Encoding.UTF8.GetBytes($"{{\"error\":\"Not implemented: {nie.Message}\"}}");
                resp.ContentLength64 = buf.Length;
                await resp.OutputStream.WriteAsync(buf, 0, buf.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HAAPI]  !! Error: {ex.Message}");
                resp.StatusCode = 500;
            }
            finally
            {
                resp.OutputStream.Close();
            }
        }

        private static string RouteHaapi(string path, string method, string body)
        {
            if (method == "OPTIONS") return "{}";

            if (method == "GET" && path == "/config/dofus3.json")
                return Dofus3ConfigResponse();

            if (method == "POST" && (path == "/json/Ankama/v5/Api/Connect" || path == "/json/Ankama/v5/Account/ApiKey" || path == "/json/Ankama/v5/Account/CreateApiKey"))
                return TokenResponse();

            if (method == "GET" && path.StartsWith("/json/Ankama/v5/Account/GetAccount"))
                return AccountResponse();

            if (method == "GET" && path == "/json/Ankama/v5/Game/ServerList")
                return GameServerListResponse();

            if (method == "POST" && path == "/json/Ankama/v5/Api/GameToken")
                return GameTokenResponse();

            if (method == "POST" && path == "/json/Ankama/v5/Game/SelectServer")
                return SelectServerResponse();

            // Return a tolerant empty JSON response for any other unhandled endpoint
            // (e.g. telemetries like SendEvent) to prevent client-side promise rejection crashes.
            Console.WriteLine($"[HAAPI Warning] Unhandled endpoint requested: {method} {path}. Returning empty JSON response for safety.");
            return "{}";
        }

        private static string Dofus3ConfigResponse() => @"{
            ""gameAppId"": 1,
            ""connectionHosts"": [
                ""JMBouftou:127.0.0.1:5555""
            ],
            ""buildType"": ""release"",
            ""chatAppId"": 99,
            ""chatServerHost"": ""127.0.0.1"",
            ""chatServerPort"": 6337,
            ""versionFileUrl"": """",
            ""haapiAnkamaUrl"": ""http://127.0.0.1:8888/json/Ankama/v5/"",
            ""haapiDofusUrl"": ""http://127.0.0.1:8888/json/Dofus/v3/"",
            ""shopiDofusUrl"": ""https://shop-api.ankama.com"",
            ""webShopDofusUrl"": ""https://store.ankama.com/"",
            ""gamesActivityDescriptorUrl"": ""https://launcher.cdn.ankama.com/configs/useractivities.json"",
            ""avatarUrlFormat"": ""https://avatar.ankama.lan/users/{0}.png"",
            ""dofusWebsiteUrl"": ""https://www.dofus.com"",
            ""local"": {
                ""build_override"": ""3.6.4"",
                ""cdn_override"": ""https://dofus2.cdn.ankama.com"",
                ""client_override"": ""es""
            },
            ""login"": {
                ""ports"": [5555],
                ""hosts"": [""127.0.0.1""]
            }
        }";

        private static string TokenResponse() => System.Text.Json.JsonSerializer.Serialize(new
        {
            token = "eb95866f-8625-47bf-a7ea-3c3ad71bac1d",
            key = "eb95866f-8625-47bf-a7ea-3c3ad71bac1d",
            expiration = "2035-01-01T00:00:00Z"
        });

        private static string AccountResponse() => @"{
            ""id"": 188940901,
            ""login"": ""jondo@emulator.com"",
            ""nickname"": ""Jondo"",
            ""tag"": ""2026"",
            ""security"": [],
            ""added_date"": ""2026-06-22T00:00:00Z"",
            ""locked"": false,
            ""parental_control"": false,
            ""avatar"": ""0"",
            ""fb_id"": null,
            ""anonymous"": false,
            ""steam_id"": null,
            ""google_id"": null,
            ""apple_id"": null,
            ""email"": ""jondo@emulator.com"",
            ""lang"": ""es"",
            ""country"": ""ES"",
            ""pioneer"": false
        }";

        private static string GameServerListResponse() => @"{
            ""servers"": [{
                ""id"": 401,
                ""name"": ""Tal Kasha"",
                ""type"": 1,
                ""status"": 3,
                ""completion"": 1,
                ""is_mono"": false,
                ""characters"": 1,
                ""date"": ""2026-06-22T00:00:00Z""
            }]
        }";

        private static string GameTokenResponse()
        {
            string token = Guid.NewGuid().ToString("N");
            DatabaseManager.SetGameToken(188940901, token);

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                token = token,
                server = new { host = "127.0.0.1", port = 5555 }
            });
        }

        private static string SelectServerResponse() => System.Text.Json.JsonSerializer.Serialize(new
        {
            token = Guid.NewGuid().ToString("N"),
            server = new { host = "127.0.0.1", port = 5555 }
        });
    }
}
