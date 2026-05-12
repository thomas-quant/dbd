using DecoyRequest.Classes;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace DecoyRequest
{
    public static class FiddlerCore
    {
        private static ProxyServer _proxy;
        private static ExplicitProxyEndPoint _endpoint;

        public static bool IsStarted => _proxy?.ProxyRunning ?? false;

        public static int ListenPort => _endpoint?.Port ?? 0;

        public static bool CheckCertificate()
        {
            if (_proxy == null)
                _proxy = CreateProxyServer();

            var cm = _proxy.CertificateManager;
            cm.EnsureRootCertificate();

            if (!cm.IsRootCertificateTrusted())
                cm.TrustRootCertificate();

            return cm.IsRootCertificateTrusted();
        }

        public static void Start(ushort port)
        {
            if (_proxy == null)
                _proxy = CreateProxyServer();

            _endpoint = new ExplicitProxyEndPoint(IPAddress.Loopback, port, decryptSsl: true);
            _proxy.AddEndPoint(_endpoint);
            _proxy.BeforeRequest += BeforeRequest;
            _proxy.BeforeResponse += BeforeResponse;
            _proxy.Start();
            _proxy.SetAsSystemProxy(_endpoint, ProxyProtocolType.AllProtocols);
        }

        public static void Stop()
        {
            if (_proxy == null) return;
            try
            {
                _proxy.UnsetAsSystemProxy();
                _proxy.BeforeRequest -= BeforeRequest;
                _proxy.BeforeResponse -= BeforeResponse;
                _proxy.Stop();
            }
            finally
            {
                _proxy.Dispose();
                _proxy = null;
                _endpoint = null;
            }
        }

        private static ProxyServer CreateProxyServer()
        {
            var server = new ProxyServer();
            server.CertificateManager.RootCertificateName = "Eclipsed Light CA";
            server.CertificateManager.RootCertificateIssuerName = "Eclipsed Light CA";
            return server;
        }

        private static async Task BeforeRequest(object sender, SessionEventArgs e)
        {
            try
            {
                if (!e.HttpClient.Request.Host.Contains("bhvrdbd.com")) return;

                #region Get BhvrSession
                if (e.HttpClient.Request.Url.Contains("/api/v1/config"))
                {
                    var cookie = e.HttpClient.Request.Headers.GetFirstHeader("Cookie")?.Value ?? string.Empty;
                    if (cookie.Length > 0)
                    {
                        var bhvrsession = cookie.Replace("bhvrSession=", string.Empty);
                        Main.instance.UpdateBhvrSession(bhvrsession);
                    }
                }
                #endregion

                #region Unlock Skins
                if (Options.UnlockAll && e.HttpClient.Request.Url.EndsWith("/api/v1/dbd-inventories/all", StringComparison.OrdinalIgnoreCase))
                {
                    var market = new MarketBuilder()
                        .WithCharacters()
                        .WithCosmetics();

                    if (Options.BloodwebExploit)
                        market.WithInventory();

                    await e.Ok(market.Build(), new List<HttpHeader> { new HttpHeader("Content-Type", "application/json") });
                    return;
                }
                #endregion

                #region Player Card
                if (Options.UnlockAll && e.HttpClient.Request.Url.Contains("api/v1/dbd-player-card"))
                {
                    if (e.HttpClient.Request.Url.EndsWith("/set"))
                    {
                        var body = await e.GetRequestBodyAsString();
                        Cache.SelectedBanner = body;
                        Main.instance.SaveSettings();
                        await e.Ok(body, new List<HttpHeader> { new HttpHeader("Content-Type", "application/json") });
                        return;
                    }
                    if (e.HttpClient.Request.Url.EndsWith("/get") && Cache.SelectedBanner != null)
                    {
                        await e.Ok(Cache.SelectedBanner, new List<HttpHeader> { new HttpHeader("Content-Type", "application/json") });
                        return;
                    }
                }
                #endregion

                #region Bloodweb Exploit — store request body for use in BeforeResponse
                if (Options.BloodwebExploit && e.HttpClient.Request.Url.Contains("api/v1/dbd-character-data/bloodweb"))
                {
                    e.UserData = await e.GetRequestBodyAsString();
                }
                #endregion
            }
            catch (Exception ex)
            {
                Main.Logs.WriteError($"<{nameof(BeforeRequest)}> An Error Occurred", ex);
            }
        }

        private static async Task BeforeResponse(object sender, SessionEventArgs e)
        {
            try
            {
                if (!e.HttpClient.Request.Host.Contains("bhvrdbd.com")) return;

                #region Bloodweb Exploit
                if (Options.BloodwebExploit)
                {
                    if (e.HttpClient.Request.Url.Contains("api/v1/dbd-character-data/get-all"))
                    {
                        var getall = JObject.Parse(Cache.CharactersData);
                        var array = getall["list"] as JArray;
                        for (int i = 0; i < array.Count; i++)
                        {
                            array[i]["prestigeLevel"] = Options.Prestige;
                            array[i]["bloodWebLevel"] = 50;
                            array[i]["legacyPrestigeLevel"] = 3;
                        }
                        e.HttpClient.Response.StatusCode = 200;
                        await e.SetResponseBodyString(getall.ToString(Newtonsoft.Json.Formatting.None));
                    }

                    if (e.HttpClient.Request.Url.Contains("api/v1/dbd-character-data/bloodweb"))
                    {
                        var reqBody = e.UserData as string ?? string.Empty;
                        var resBody = await e.GetResponseBodyAsString();
                        if (reqBody.TryParseJObject(out var json_Request) && resBody.TryParseJObject(out _))
                        {
                            var charName = (string)json_Request["characterName"];
                            var result_Response = JObject.Parse(Cache.CharacterData);
                            result_Response["characterItems"] = new MarketBuilder().BEWInventory();
                            result_Response["characterName"] = charName;
                            result_Response["prestigeLevel"] = Options.Prestige;
                            result_Response["bloodWebLevel"] = Options.BloodWebLevel;
                            result_Response["legacyPrestigeLevel"] = Options.LegacyPrestigeLevel;

                            e.HttpClient.Response.StatusCode = 200;
                            await e.SetResponseBodyString(result_Response.ToString(Newtonsoft.Json.Formatting.None));
                        }
                    }
                }
                #endregion

                #region Ban Status
                if (e.HttpClient.Request.Url.Contains("api/v1/players/ban/status"))
                {
                    var body = await e.GetResponseBodyAsString();
                    Main.instance.UpdateBanStatus(body.Contains("\"isBanned\":true"));
                }
                #endregion
            }
            catch (Exception ex)
            {
                Main.Logs.WriteError($"<{nameof(BeforeResponse)}> An Error Occurred", ex);
            }
        }
    }
}
