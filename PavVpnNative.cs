using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Web.Script.Serialization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("PAVVPN Native")]
[assembly: System.Reflection.AssemblyDescription("Discord-only native user-mode TLS record proxy")]
[assembly: System.Reflection.AssemblyCompany("PAVVPN contributors")]
[assembly: System.Reflection.AssemblyProduct("PAVVPN")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 PAVVPN contributors")]
[assembly: System.Reflection.AssemblyVersion("5.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("5.0.0.0")]

namespace PavDiscord
{
    // User-space proxy: no system interception, DNS changes or drivers.
    class Program
    {
        const int Port = 1088;
        const string Version = "PAVVPN/5.0-native";
        const int MaxConnections = 256;
        static readonly object LogLock = new object();
        static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        static readonly ConcurrentDictionary<string, DnsEntry> Cache = new ConcurrentDictionary<string, DnsEntry>(StringComparer.OrdinalIgnoreCase);
        static readonly string[] CheckHosts = { "discord.com", "updates.discord.com", "cdn.discordapp.com", "gateway.discord.gg" };
        static readonly string UpdaterToken = Guid.NewGuid().ToString("N");
        static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord", "settings.json");
        static readonly string OverrideBackup = Path.Combine(BaseDir, "pavdns-v4-override.json");
        static readonly string NetworkBackup = Path.Combine(BaseDir, "pavvpn-network-settings.json");
        static readonly string ControlTokenPath = Path.Combine(BaseDir, "pavvpn-control.token");
        static string ControlToken;
        static volatile bool Ready;
        const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        const string PacUrl = "http://127.0.0.1:1088/pavdns.pac";
        static readonly object SettingsLock = new object();
        static readonly object NetworkSettingsLock = new object();
        static readonly SemaphoreSlim ConnectionSlots = new SemaphoreSlim(MaxConnections, MaxConnections);
        delegate bool ConsoleHandler(uint type);
        static readonly ConsoleHandler CloseHandler = OnConsoleClose;
        [DllImport("Kernel32.dll")] static extern bool SetConsoleCtrlHandler(ConsoleHandler handler, bool add);
        [DllImport("Kernel32.dll", SetLastError = true)] static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
        [DllImport("Kernel32.dll")] static extern IntPtr GetStdHandle(int handle);
        [DllImport("Kernel32.dll")] static extern bool GetConsoleMode(IntPtr handle, out uint mode);
        [DllImport("Kernel32.dll")] static extern bool SetConsoleMode(IntPtr handle, uint mode);
        [DllImport("wininet.dll", SetLastError = true)] static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int length);
        static void NoInherit(Socket socket)
        {
            if (!SetHandleInformation(socket.Handle, 1, 0)) throw new IOException("Soket kalitim ayari yapilamadi.");
        }
        class DnsEntry { public string[] Addresses; public DateTime Expires; }

        static void Log(string message)
        {
            lock (LogLock)
            {
                string line = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, message);
                Console.WriteLine(line);
                try
                {
                    string path = Path.Combine(BaseDir, "pav_debug.log");
                    if (File.Exists(path) && new FileInfo(path).Length >= 2 * 1024 * 1024)
                    {
                        string previous = Path.Combine(BaseDir, "pav_debug.previous.log");
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(path, previous);
                    }
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch { }
            }
        }

        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 64;
            try { Console.Title = "PAVVPN v5 Native - Surucusuz Discord Proxy"; } catch (IOException) { }
            uint consoleMode;
            IntPtr consoleInput = GetStdHandle(-10);
            if (GetConsoleMode(consoleInput, out consoleMode)) SetConsoleMode(consoleInput, (consoleMode | 0x80) & ~0x40u);
            bool check = Array.IndexOf(args, "--check") >= 0;
            bool probe = Array.IndexOf(args, "--probe") >= 0;
            bool launch = !probe && Array.IndexOf(args, "--proxy-only") < 0;
            if (Array.IndexOf(args, "--self-test") >= 0) return SelfTest();
            if (Array.IndexOf(args, "--stop") >= 0) return RequestControl("stop") ? 0 : 1;
            if (check) return CheckConnection() ? 0 : 1;
            StopPreviousV4();
            Console.WriteLine("PAVVPN v5 Native - Discord'a ozel / Surucusuz / Harici motor yok");
            Console.WriteLine("Yonetici gerekmez. Yalniz Discord icin kullanici PAC ayari uygular.");
            Console.WriteLine("Bu pencere acik kalmali. Kapatmak tuneli durdurur.");
            if (IsOurProxy())
            {
                if (launch && CheckConnection()) return RequestControl("launch") ? 0 : 1;
                Log("PavDiscord v4 zaten acik."); return 1;
            }
            TcpListener listener = new TcpListener(IPAddress.Loopback, Port);
            try { listener.Start(); NoInherit(listener.Server); }
            catch (Exception ex)
            {
                Log("1088 portu acilamadi: " + ex.Message);
                Log("Eski PavDiscord pencerelerini kapatip tekrar deneyin."); return 1;
            }
            try { ControlToken = CreateControlToken(); }
            catch (Exception ex) { Log("Kontrol tokeni olusturulamadi: " + ex.Message); listener.Stop(); return 1; }
            Log("Dinleniyor: 127.0.0.1:1088 - " + Version);
            RestoreLegacySetting();
            RestoreUpdaterSetting();
            RestoreUserPacSetting();
            SetConsoleCtrlHandler(CloseHandler, true);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Cleanup();
            Task.Run(() => AcceptClients(listener));
            if (!CheckConnection())
            {
                Log("Baglanti dogrulanamadi; Discord baslatilmadi. pav_debug.log dosyasina bakin.");
                listener.Stop(); return 1;
            }
            if (!probe)
            {
                try { EnableUserPacSetting(); }
                catch (Exception ex) { Log("Chrome Discord ayari uygulanamadi: " + ex.Message); listener.Stop(); return 1; }
            }
            Ready = true;
            if (launch && !LaunchDiscord())
                Log("Discord otomatik baslatilamadi; PAVVPN motoru web ve sonraki Discord acilisi icin etkin tutuluyor.");
            while (true) Thread.Sleep(1000);
        }

        static void AcceptClients(TcpListener listener)
        {
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                Task.Run(async () =>
                {
                    using (client)
                    {
                        if (!await ConnectionSlots.WaitAsync(0))
                        {
                            Log("Baglanti siniri dolu; yeni yerel istemci reddedildi.");
                            return;
                        }
                        try { NoInherit(client.Client); await HandleClient(client); }
                        catch (Exception ex) { Log("Baglanti: " + ex.Message); }
                        finally { ConnectionSlots.Release(); }
                    }
                });
            }
        }

        static async Task HandleClient(TcpClient client)
        {
            client.NoDelay = true;
            using (var timer = new Timer(_ => client.Close(), null, 15000, Timeout.Infinite))
            {
                NetworkStream stream = client.GetStream();
                byte[] first = new byte[1];
                if (!await ReadExact(stream, first, 0, 1)) return;
                bool http = first[0] != 5;
                string host;
                int port;
                if (!http)
                {
                    byte[] count = new byte[1];
                    if (!await ReadExact(stream, count, 0, 1) || count[0] == 0) return;
                    byte[] methods = new byte[count[0]];
                    if (!await ReadExact(stream, methods, 0, methods.Length)) return;
                    bool noAuth = Array.IndexOf(methods, (byte)0) >= 0;
                    await stream.WriteAsync(new byte[] { 5, noAuth ? (byte)0 : (byte)255 }, 0, 2);
                    if (!noAuth) return;
                    byte[] header = new byte[4];
                    if (!await ReadExact(stream, header, 0, 4)) return;
                    if (header[0] != 5 || header[1] != 1 || header[2] != 0)
                    { await SocksReply(stream, 7); return; }
                    if (header[3] == 3)
                    {
                        if (!await ReadExact(stream, count, 0, 1) || count[0] == 0) return;
                        byte[] domain = new byte[count[0]];
                        if (!await ReadExact(stream, domain, 0, domain.Length)) return;
                        host = Encoding.ASCII.GetString(domain);
                    }
                    else if (header[3] == 1 || header[3] == 4)
                    {
                        byte[] address = new byte[header[3] == 1 ? 4 : 16];
                        if (!await ReadExact(stream, address, 0, address.Length)) return;
                        host = new IPAddress(address).ToString();
                    }
                    else { await SocksReply(stream, 8); return; }
                    byte[] portBytes = new byte[2];
                    if (!await ReadExact(stream, portBytes, 0, 2)) return;
                    port = (portBytes[0] << 8) | portBytes[1];
                }
                else
                {
                    var header = new List<byte> { first[0] };
                    while (header.Count < 8192)
                    {
                        if (!await ReadExact(stream, first, 0, 1)) return;
                        header.Add(first[0]);
                        int n = header.Count;
                        if (n >= 4 && header[n-4] == 13 && header[n-3] == 10 && header[n-2] == 13 && header[n-1] == 10) break;
                    }
                    string text = Encoding.ASCII.GetString(header.ToArray());
                    if (text.StartsWith("GET /pavdns-version HTTP/"))
                    { await HttpReply(stream, "200 OK", Version); return; }
                    if (text.StartsWith("GET /pavdns-ready HTTP/"))
                    { await HttpReply(stream, Ready ? "200 OK" : "503 Service Unavailable", Ready ? Version : "STARTING"); return; }
                    if (text.StartsWith("GET /pavdns.pac HTTP/"))
                    { await HttpReply(stream, "200 OK", ProxyPac(), "application/x-ns-proxy-autoconfig"); return; }
                    if (text.StartsWith("POST /pavdns-", StringComparison.Ordinal) && !string.IsNullOrEmpty(ControlToken) && text.IndexOf("\r\nX-PavDNS-Control: " + ControlToken + "\r\n", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (text.StartsWith("POST /pavdns-launch HTTP/", StringComparison.Ordinal))
                        {
                            bool started = LaunchDiscord();
                            await HttpReply(stream, started ? "200 OK" : "500 Internal Server Error", started ? "OK" : "Discord acilamadi.");
                            return;
                        }
                        if (text.StartsWith("POST /pavdns-stop HTTP/", StringComparison.Ordinal))
                        {
                            Ready = false;
                            RestoreUpdaterSetting();
                            RestoreUserPacSetting();
                            await HttpReply(stream, "200 OK", "OK");
                            QueueExit();
                            return;
                        }
                    }
                    if (!text.EndsWith("\r\n\r\n")) { await HttpReply(stream, "431 Request Header Fields Too Large", ""); return; }
                    string[] parts = text.Split(new char[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 && (parts[0] == "GET" || parts[0] == "HEAD") && parts[1].StartsWith("/pav-updater/" + UpdaterToken + "/", StringComparison.Ordinal))
                    {
                        timer.Change(Timeout.Infinite, Timeout.Infinite);
                        await ServeUpdater(stream, parts[0], parts[1]);
                        return;
                    }
                    if (parts.Length < 3 || parts[0] != "CONNECT")
                    { await HttpReply(stream, "405 Method Not Allowed", "HTTPS CONNECT gerekir."); return; }
                    string authority = parts[1];
                    int colon = authority.LastIndexOf(':');
                    if (colon <= 0 || !int.TryParse(authority.Substring(colon + 1), out port))
                    { await HttpReply(stream, "400 Bad Request", ""); return; }
                    host = authority.Substring(0, colon).Trim('[', ']');
                }
                if (port <= 0 || port > 65535) return;
                if (!AllowedDiscordTarget(host, port))
                {
                    Log("Engellenen hedef: " + host + ":" + port);
                    if (http) await HttpReply(stream, "403 Forbidden", "Yalnizca Discord HTTPS trafigi.");
                    else await SocksReply(stream, 2);
                    return;
                }
                TcpClient remote = null;
                try { remote = await ConnectHost(host, port); }
                catch (Exception ex)
                {
                    Log("Hedefe ulasilamadi " + host + ": " + ex.Message);
                }
                if (remote == null)
                {
                    if (http) await HttpReply(stream, "502 Bad Gateway", "");
                    else await SocksReply(stream, 4);
                    return;
                }
                using (remote)
                {
                    NetworkStream target = remote.GetStream();
                    if (http)
                    {
                        byte[] ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                        await stream.WriteAsync(ok, 0, ok.Length);
                    }
                    else await SocksReply(stream, 0);
                    byte[] payload = await ReadFirstPayload(stream);
                    if (payload.Length == 0) return;
                    byte[] transformed = SplitTlsClientHello(payload);
                    await target.WriteAsync(transformed, 0, transformed.Length);
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                    Task up = stream.CopyToAsync(target);
                    Task down = target.CopyToAsync(stream);
                    await Task.WhenAny(up, down);
                    remote.Close(); client.Close();
                    try { await Task.WhenAll(up, down); } catch { }
                }
            }
        }

        static async Task<byte[]> ReadFirstPayload(Stream stream)
        {
            byte[] first = new byte[1];
            if (!await ReadExact(stream, first, 0, 1)) return new byte[0];
            if (first[0] != 0x16) return first;
            byte[] header = new byte[5]; header[0] = first[0];
            if (!await ReadExact(stream, header, 1, 4)) throw new IOException("Eksik TLS basligi.");
            int size = (header[3] << 8) | header[4];
            if (header[1] != 3 || size == 0 || size > 18432) throw new IOException("Gecersiz TLS kaydi.");
            byte[] record = new byte[size + 5];
            Buffer.BlockCopy(header, 0, record, 0, 5);
            if (!await ReadExact(stream, record, 5, size)) throw new IOException("Eksik TLS kaydi.");
            return record;
        }

        // Re-encodes one ClientHello as two valid TLS records. The split is placed
        // three bytes into the visible SNI host. No packet driver or raw socket is used.
        static byte[] SplitTlsClientHello(byte[] record)
        {
            int hostOffset, hostLength;
            if (!TryFindSni(record, out hostOffset, out hostLength) || hostLength < 4) return record;
            int payloadLength = (record[3] << 8) | record[4];
            int split = hostOffset + 3 - 5;
            if (split <= 0 || split >= payloadLength) return record;
            byte[] output = new byte[record.Length + 5];
            Buffer.BlockCopy(record, 0, output, 0, 5);
            output[3] = (byte)(split >> 8); output[4] = (byte)split;
            Buffer.BlockCopy(record, 5, output, 5, split);
            int second = 5 + split;
            output[second] = record[0]; output[second + 1] = record[1]; output[second + 2] = record[2];
            int remaining = payloadLength - split;
            output[second + 3] = (byte)(remaining >> 8); output[second + 4] = (byte)remaining;
            Buffer.BlockCopy(record, 5 + split, output, second + 5, remaining);
            return output;
        }

        static bool TryFindSni(byte[] data, out int hostOffset, out int hostLength)
        {
            hostOffset = 0; hostLength = 0;
            if (data == null || data.Length < 50 || data[0] != 0x16 || data[1] != 3 || data[5] != 1) return false;
            int recordLength = (data[3] << 8) | data[4];
            if (recordLength + 5 != data.Length) return false;
            int p = 9;
            if (p + 34 > data.Length) return false;
            p += 34;
            if (p >= data.Length) return false;
            int sessionLength = data[p++];
            if (p + sessionLength + 2 > data.Length) return false;
            p += sessionLength;
            int cipherLength = (data[p] << 8) | data[p + 1]; p += 2;
            if (p + cipherLength + 1 > data.Length) return false;
            p += cipherLength;
            int compressionLength = data[p++];
            if (p + compressionLength + 2 > data.Length) return false;
            p += compressionLength;
            int extensionsLength = (data[p] << 8) | data[p + 1]; p += 2;
            int extensionsEnd = p + extensionsLength;
            if (extensionsEnd > data.Length) return false;
            while (p + 4 <= extensionsEnd)
            {
                int type = (data[p] << 8) | data[p + 1];
                int length = (data[p + 2] << 8) | data[p + 3];
                int body = p + 4;
                if (body + length > extensionsEnd) return false;
                if (type == 0 && length >= 5)
                {
                    int listLength = (data[body] << 8) | data[body + 1];
                    int nameType = data[body + 2];
                    int nameLength = (data[body + 3] << 8) | data[body + 4];
                    if (nameType != 0 || listLength + 2 > length || nameLength + 5 > length) return false;
                    hostOffset = body + 5; hostLength = nameLength;
                    return hostOffset + hostLength <= data.Length;
                }
                p = body + length;
            }
            return false;
        }

        static string[] ResolveHost(string host)
        {
            IPAddress address;
            if (IPAddress.TryParse(host, out address)) return new string[] { host };
            DnsEntry cached;
            if (Cache.TryGetValue(host, out cached) && cached.Expires > DateTime.UtcNow) return cached.Addresses;
            Exception last = null;
            foreach (string resolver in new string[] { "https://1.1.1.1/dns-query", "https://dns.google/resolve" })
            {
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(resolver + "?name=" + Uri.EscapeDataString(host) + "&type=A");
                    request.Accept = "application/dns-json"; request.Proxy = null;
                    request.Timeout = 5000; request.ReadWriteTimeout = 5000;
                    using (var response = request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        var json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                        object answers;
                        var ips = new List<string>(); int ttl = 300;
                        if (Convert.ToInt32(json["Status"]) != 0) throw new IOException("DNS cevabi basarisiz.");
                        if (json.TryGetValue("Answer", out answers))
                        foreach (object answer in (System.Collections.IEnumerable)answers)
                        {
                            var record = answer as Dictionary<string, object>;
                            if (record == null || Convert.ToInt32(record["type"]) != 1) continue;
                            string ip = Convert.ToString(record["data"]);
                            if (!IPAddress.TryParse(ip, out address) || address.AddressFamily != AddressFamily.InterNetwork) continue;
                            if (!ips.Contains(ip)) ips.Add(ip);
                            ttl = Math.Min(ttl, Math.Max(1, Convert.ToInt32(record["TTL"])));
                        }
                        if (ips.Count == 0) throw new IOException("DNS A kaydi bulunamadi.");
                        cached = new DnsEntry { Addresses = ips.ToArray(), Expires = DateTime.UtcNow.AddSeconds(ttl) };
                        Cache[host] = cached;
                        Log("DNS -> " + host + ": " + string.Join(", ", cached.Addresses));
                        return cached.Addresses;
                    }
                }
                catch (Exception ex) { last = ex; }
            }
            throw new IOException("Guvenli DNS cozumlenemedi: " + host, last);
        }

        static async Task<TcpClient> ConnectHost(string host, int port)
        {
            string[] ips = await Task.Run(() => ResolveHost(host));
            Exception last = null;
            foreach (string ip in ips)
            {
                IPAddress address = IPAddress.Parse(ip);
                var client = new TcpClient(address.AddressFamily) { NoDelay = true };
                try
                {
                    NoInherit(client.Client);
                    Task connect = client.ConnectAsync(address, port);
                    if (await Task.WhenAny(connect, Task.Delay(4000)) != connect) throw new TimeoutException("TCP zaman asimi.");
                    await connect;
                    return client;
                }
                catch (Exception ex) { last = ex; client.Close(); }
            }
            throw new IOException("Tum hedef IP adresleri basarisiz.", last);
        }

        static async Task<bool> ReadExact(Stream stream, byte[] data, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(data, offset + read, count - read);
                if (n == 0) return false;
                read += n;
            }
            return true;
        }
        static Task SocksReply(Stream stream, byte status)
        { return stream.WriteAsync(new byte[] { 5, status, 0, 1, 127, 0, 0, 1, 4, 56 }, 0, 10); }
        static Task HttpReply(Stream stream, string status, string body)
        { return HttpReply(stream, status, body, "text/plain; charset=us-ascii"); }
        static Task HttpReply(Stream stream, string status, string body, string contentType)
        {
            byte[] bytes = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nConnection: close\r\nContent-Type: " + contentType + "\r\nCache-Control: no-store\r\nContent-Length: " + Encoding.ASCII.GetByteCount(body) + "\r\n\r\n" + body);
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }
        static string ProxyPac()
        {
            return "function FindProxyForURL(url, host) {\n" +
                "  host = host.toLowerCase();\n" +
                "  var roots = ['discord.com','discord.gg','discordapp.com','discordapp.net','discord.media','dis.gd'];\n" +
                "  for (var i = 0; i < roots.length; i++) {\n" +
                "    if (host === roots[i] || dnsDomainIs(host, '.' + roots[i])) return 'PROXY 127.0.0.1:1088';\n" +
                "  }\n" +
                "  return 'DIRECT';\n" +
                "}\n";
        }
        static bool IsOurProxy()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:1088/pavdns-version");
                req.Proxy = null; req.Timeout = 1500; req.ReadWriteTimeout = 1500;
                using (var res = req.GetResponse())
                using (var reader = new StreamReader(res.GetResponseStream())) return reader.ReadToEnd() == Version;
            }
            catch { return false; }
        }
        static void StopPreviousV4()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:1088/pavdns-version");
                req.Proxy = null; req.Timeout = 1500; req.ReadWriteTimeout = 1500;
                string previous;
                using (var response = req.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream())) previous = reader.ReadToEnd();
                if ((!previous.StartsWith("PAVDNS/4.", StringComparison.Ordinal) && !previous.StartsWith("PAVVPN/5.", StringComparison.Ordinal)) || previous == Version) return;
                var stop = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:1088/pavdns-stop");
                stop.Proxy = null; stop.Method = "POST"; stop.ContentLength = 0;
                string token = previous.StartsWith("PAVVPN/5.", StringComparison.Ordinal) && File.Exists(ControlTokenPath) ? File.ReadAllText(ControlTokenPath).Trim() : "4.0";
                stop.Headers["X-PavDNS-Control"] = token; stop.Timeout = 3000;
                using (var response = stop.GetResponse()) { }
                Thread.Sleep(1000);
            }
            catch { }
        }
        static bool RequestControl(string action)
        {
            try
            {
                if (!IsOurProxy() && action != "stop")
                { Log("PavDiscord v4 calismiyor veya farkli surum acik."); return false; }
                if (action == "stop")
                {
                    var versionRequest = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:1088/pavdns-version");
                    versionRequest.Proxy = null; versionRequest.Timeout = 1500;
                    string running;
                    using (var versionResponse = versionRequest.GetResponse())
                    using (var reader = new StreamReader(versionResponse.GetResponseStream())) running = reader.ReadToEnd();
                    if (!running.StartsWith("PAVDNS/4.", StringComparison.Ordinal) && !running.StartsWith("PAVVPN/5.", StringComparison.Ordinal))
                    { Log("1088 portundaki uygulama PAVVPN degil; dokunulmadi."); return false; }
                }
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:1088/pavdns-" + action);
                req.Proxy = null; req.Method = "POST"; req.ContentLength = 0;
                if (!File.Exists(ControlTokenPath)) { Log("Kontrol tokeni bulunamadi."); return false; }
                req.Headers["X-PavDNS-Control"] = File.ReadAllText(ControlTokenPath).Trim();
                req.Timeout = 15000; req.ReadWriteTimeout = 15000;
                using (var res = req.GetResponse())
                using (var reader = new StreamReader(res.GetResponseStream())) return reader.ReadToEnd() == "OK";
            }
            catch (Exception ex) { Log("Kontrol istegi basarisiz: " + ex.Message); return false; }
        }
        static bool CheckConnection()
        {
            bool success = true;
            foreach (string host in CheckHosts)
            {
                try
                {
                    string path = host == "discord.com" ? "/api/v10/gateway" : "/";
                    var request = (HttpWebRequest)WebRequest.Create("https://" + host + path);
                    request.Proxy = new WebProxy("http://127.0.0.1:1088");
                    request.Timeout = 12000; request.ReadWriteTimeout = 12000;
                    request.AllowAutoRedirect = false;
                    request.Method = host == "discord.com" ? "GET" : "HEAD";
                    HttpWebResponse response;
                    try { response = (HttpWebResponse)request.GetResponse(); }
                    catch (WebException ex)
                    {
                        if (ex.Status != WebExceptionStatus.ProtocolError || ex.Response == null) throw;
                        response = (HttpWebResponse)ex.Response;
                    }
                    using (response)
                    {
                        if (host == "discord.com")
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            if (response.StatusCode != HttpStatusCode.OK || !reader.ReadToEnd().Contains("wss://gateway.discord.gg"))
                                throw new IOException("Discord gateway API cevabi dogrulanamadi.");
                        }
                        Log("[OK] " + host + " - sertifika dogrulandi, HTTP " + (int)response.StatusCode);
                    }
                }
                catch (Exception ex) { Log("[HATA] " + host + " - " + ex.Message); success = false; }
            }
            return success;
        }
        static bool LaunchDiscord()
        {
            string stage = "Discord klasoru aranirken";
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
                string best = null;
                System.Version bestVersion = new System.Version(0, 0);
                foreach (string app in Directory.GetDirectories(dir, "app-*"))
                {
                    System.Version version;
                    string exe = Path.Combine(app, "Discord.exe");
                    if (System.Version.TryParse(Path.GetFileName(app).Substring(4), out version) && version > bestVersion && File.Exists(exe))
                    { bestVersion = version; best = exe; }
                }
                if (best == null) throw new IOException("Kurulu Discord uygulamasi bulunamadi.");
                stage = "acik Discord islemleri kapatilirken";
                foreach (Process process in Process.GetProcesses())
                using (process)
                {
                    if (!process.ProcessName.Equals("Discord", StringComparison.OrdinalIgnoreCase) && !process.ProcessName.Equals("Update", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        string path = process.MainModule.FileName;
                        if (!path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                        process.Kill(); process.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        // Killing the parent also closes child processes from our initial snapshot.
                        if (ex is InvalidOperationException || ex is ArgumentException) continue;
                        // A different elevated program may also be named Update.exe. If its
                        // path cannot be inspected, it is not proven to belong to Discord and
                        // must neither be killed nor allowed to stop the local proxy.
                        if (ex is System.ComponentModel.Win32Exception) continue;
                        try { if (process.HasExited) continue; } catch (InvalidOperationException) { continue; }
                        Log("Eski Discord islemi kapatilamadi: " + ex.Message); return false;
                    }
                }
                var start = new ProcessStartInfo(best, "--proxy-server=http://127.0.0.1:1088 --disable-quic");
                // Shell launch avoids inheriting proxy/socket/console handles into Discord.
                start.UseShellExecute = true;
                start.WorkingDirectory = Path.GetDirectoryName(best);
                stage = "gecici Discord guncelleme ayari yazilirken";
                SetUpdaterSetting();
                stage = "Discord.exe baslatilirken";
                Process.Start(start);
                Log("Discord proxy ile baslatildi. Sesli kanalin UDP trafigi bu TCP proxy'den gecmez.");
                return true;
            }
            catch (Exception ex) { RestoreUpdaterSetting(); Log("Discord acilamadi (" + stage + "): " + ex.Message); return false; }
        }

        static string UpdaterBase { get { return "http://127.0.0.1:1088/pav-updater/" + UpdaterToken + "/"; } }
        static void QueueExit() { Task.Run(async () => { await Task.Delay(300); Environment.Exit(0); }); }
        static bool OnConsoleClose(uint type)
        {
            Cleanup();
            return false;
        }
        static bool IsDiscordHost(string host)
        {
            foreach (string suffix in new string[] { "discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media", "dis.gd" })
                if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        static bool AllowedDiscordTarget(string host, int port)
        {
            if (!IsDiscordHost(host)) return false;
            if (port == 443) return true;
            if (!host.EndsWith(".discord.media", StringComparison.OrdinalIgnoreCase)) return false;
            return port == 2053 || port == 2083 || port == 2087 || port == 2096 || port == 8443;
        }
        static void Cleanup()
        {
            Ready = false;
            RestoreUpdaterSetting();
            RestoreUserPacSetting();
            try
            {
                if (File.Exists(ControlTokenPath) && !string.IsNullOrEmpty(ControlToken) && File.ReadAllText(ControlTokenPath).Trim() == ControlToken)
                    File.Delete(ControlTokenPath);
            }
            catch { }
        }
        static string CreateControlToken()
        {
            byte[] bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            string token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            File.WriteAllText(ControlTokenPath, token, new UTF8Encoding(false));
            return token;
        }
        static void NotifyProxySettingsChanged()
        {
            InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
        }
        static void EnableUserPacSetting()
        {
            lock (NetworkSettingsLock)
            {
                RestoreUserPacSetting();
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(InternetSettingsKey))
                {
                    if (key == null) throw new IOException("Kullanici internet ayarlari acilamadi.");
                    bool hadValue = Array.IndexOf(key.GetValueNames(), "AutoConfigURL") >= 0;
                    object oldValue = hadValue ? key.GetValue("AutoConfigURL", null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
                    var backup = new Dictionary<string, object> { { "hadValue", hadValue }, { "oldValue", oldValue }, { "endpoint", PacUrl } };
                    var serializer = new JavaScriptSerializer();
                    File.WriteAllText(NetworkBackup, serializer.Serialize(backup), new UTF8Encoding(false));
                    key.SetValue("AutoConfigURL", PacUrl, RegistryValueKind.String);
                }
                NotifyProxySettingsChanged();
                Log("Normal Chrome icin yalniz Discord alan adlarini kapsayan PAC ayari etkin.");
            }
        }
        static void RestoreUserPacSetting()
        {
            lock (NetworkSettingsLock)
            {
                if (!File.Exists(NetworkBackup)) return;
                try
                {
                    var serializer = new JavaScriptSerializer();
                    var backup = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(NetworkBackup));
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(InternetSettingsKey))
                    {
                        if (key == null) throw new IOException("Kullanici internet ayarlari acilamadi.");
                        object current = key.GetValue("AutoConfigURL", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (Convert.ToString(current) == Convert.ToString(backup["endpoint"]))
                        {
                            if (Convert.ToBoolean(backup["hadValue"])) key.SetValue("AutoConfigURL", Convert.ToString(backup["oldValue"]), RegistryValueKind.String);
                            else key.DeleteValue("AutoConfigURL", false);
                            NotifyProxySettingsChanged();
                        }
                    }
                    File.Delete(NetworkBackup);
                }
                catch (Exception ex) { Log("Chrome PAC ayari geri alinamadi: " + ex.Message); }
            }
        }
        static void RestoreLegacySetting()
        {
            string legacy = Path.Combine(BaseDir, "discord-updater-override.json");
            if (!File.Exists(legacy)) return;
            try
            {
                var serializer = new JavaScriptSerializer();
                var backup = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(legacy));
                var settings = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsPath));
                object current;
                if (settings.TryGetValue("NEW_UPDATE_ENDPOINT", out current) && Convert.ToString(current) == Convert.ToString(backup["endpoint"]))
                {
                    if (Convert.ToBoolean(backup["hadValue"])) settings["NEW_UPDATE_ENDPOINT"] = backup["oldValue"];
                    else settings.Remove("NEW_UPDATE_ENDPOINT");
                }
                object switchesObject;
                if (settings.TryGetValue("chromiumSwitches", out switchesObject))
                {
                    var switches = switchesObject as Dictionary<string, object>;
                    if (switches != null && switches.TryGetValue("proxy-server", out current) && Convert.ToString(current) == "http://127.0.0.1:1080")
                    {
                        if (backup.ContainsKey("hadProxySwitch") && Convert.ToBoolean(backup["hadProxySwitch"])) switches["proxy-server"] = backup["oldProxySwitch"];
                        else switches.Remove("proxy-server");
                        if (backup.ContainsKey("hadQuicSwitch") && Convert.ToBoolean(backup["hadQuicSwitch"])) switches["disable-quic"] = backup["oldQuicSwitch"];
                        else switches.Remove("disable-quic");
                    }
                }
                WriteSettings(serializer.Serialize(settings));
                File.Delete(legacy);
                Log("Eski v3 Discord ayari geri alindi.");
            }
            catch (Exception ex) { throw new IOException("Eski ayar geri alinamadi.", ex); }
        }
        static void SetUpdaterSetting()
        {
            lock (SettingsLock)
            {
                RestoreUpdaterSetting();
                var serializer = new JavaScriptSerializer();
                var settings = File.Exists(SettingsPath) ? serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsPath)) : new Dictionary<string, object>();
                object old;
                bool hadValue = settings.TryGetValue("NEW_UPDATE_ENDPOINT", out old);
                var backup = new Dictionary<string, object> { { "hadValue", hadValue }, { "oldValue", old }, { "endpoint", UpdaterBase } };
                File.WriteAllText(OverrideBackup, serializer.Serialize(backup));
                settings["NEW_UPDATE_ENDPOINT"] = UpdaterBase;

                WriteSettings(serializer.Serialize(settings));
                Log("Discord guncelleme koprusu etkin. Onceki Discord ayari kapanista geri alinacak.");
            }
        }
        static void WriteSettings(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            string temporary = SettingsPath + ".pavdns.tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
            else File.Move(temporary, SettingsPath);
        }
        static void RestoreUpdaterSetting()
        {
            lock (SettingsLock)
            {
                if (!File.Exists(OverrideBackup)) return;
                try
                {
                    var serializer = new JavaScriptSerializer();
                    var backup = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(OverrideBackup));
                    var settings = File.Exists(SettingsPath) ? serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsPath)) : new Dictionary<string, object>();
                    object current;
                    bool changed = false;
                    if (settings.TryGetValue("NEW_UPDATE_ENDPOINT", out current) && Convert.ToString(current) == Convert.ToString(backup["endpoint"]))
                    {
                        if (Convert.ToBoolean(backup["hadValue"])) settings["NEW_UPDATE_ENDPOINT"] = backup["oldValue"];
                        else settings.Remove("NEW_UPDATE_ENDPOINT");
                        changed = true;
                    }
                    object switchesObject;
                    settings.TryGetValue("chromiumSwitches", out switchesObject);
                    var switches = switchesObject as Dictionary<string, object>;
                    if (switches != null && backup.ContainsKey("hadProxySwitch") && switches.TryGetValue("proxy-server", out current) && Convert.ToString(current) == "http://127.0.0.1:1088")
                    {
                        if (Convert.ToBoolean(backup["hadProxySwitch"])) switches["proxy-server"] = backup["oldProxySwitch"];
                        else switches.Remove("proxy-server");
                        if (switches.TryGetValue("disable-quic", out current) && Convert.ToString(current) == "")
                        {
                            if (Convert.ToBoolean(backup["hadQuicSwitch"])) switches["disable-quic"] = backup["oldQuicSwitch"];
                            else switches.Remove("disable-quic");
                        }
                        changed = true;
                    }
                    if (changed) WriteSettings(serializer.Serialize(settings));
                    File.Delete(OverrideBackup);
                }
                catch (Exception ex) { Log("Discord ayari geri alinamadi: " + ex.Message); }
            }
        }
        static bool IsDiscordDownload(Uri uri)
        {
            if (uri.Scheme != "https" || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo)) return false;
            string h = uri.DnsSafeHost;
            return h == "discord.com" || h.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase) || h == "discordapp.net" || h.EndsWith(".discordapp.net", StringComparison.OrdinalIgnoreCase) || h == "discordapp.com" || h.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase);
        }
        static void RewriteDownloadUrls(object node)
        {
            var map = node as Dictionary<string, object>;
            if (map != null)
            {
                foreach (string key in new List<string>(map.Keys))
                {
                    Uri uri;
                    string value = map[key] as string;
                    if (key == "url" && value != null && Uri.TryCreate(value, UriKind.Absolute, out uri) && IsDiscordDownload(uri))
                        map[key] = UpdaterBase + "files/" + Uri.EscapeDataString(value);
                    else RewriteDownloadUrls(map[key]);
                }
            }
            else
            {
                var array = node as object[];
                if (array != null) foreach (object item in array) RewriteDownloadUrls(item);
            }
        }
        static async Task ServeUpdater(Stream stream, string method, string path)
        {
            string relative = path.Substring(("/pav-updater/" + UpdaterToken + "/").Length);
            Uri destination;
            bool manifest = !relative.StartsWith("files/", StringComparison.Ordinal);
            if (manifest) destination = new Uri("https://updates.discord.com/" + relative);
            else if (!Uri.TryCreate(Uri.UnescapeDataString(relative.Substring(6)), UriKind.Absolute, out destination))
            { await HttpReply(stream, "400 Bad Request", ""); return; }
            if (!IsDiscordDownload(destination)) { await HttpReply(stream, "403 Forbidden", ""); return; }
            var request = (HttpWebRequest)WebRequest.Create(destination);
            request.Proxy = new WebProxy("http://127.0.0.1:1088");
            request.Method = method;
            request.Timeout = 30000; request.ReadWriteTimeout = 30000;
            request.AllowAutoRedirect = false;
            HttpWebResponse response = null;
            Exception failure = null;
            try { response = (HttpWebResponse)await Task.Run(() => request.GetResponse()); }
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.ProtocolError) response = ex.Response as HttpWebResponse;
                else failure = ex;
            }
            if (response == null)
            {
                Log("Guncelleme koprusu: " + (failure == null ? "bos cevap" : failure.Message));
                await HttpReply(stream, "502 Bad Gateway", ""); return;
            }
            using (response)
            {
                if (manifest && method == "GET" && response.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
                    object json;
                    using (var reader = new StreamReader(response.GetResponseStream())) json = serializer.DeserializeObject(await reader.ReadToEndAsync());
                    RewriteDownloadUrls(json);
                    byte[] body = Encoding.UTF8.GetBytes(serializer.Serialize(json));
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + (int)response.StatusCode + " " + response.StatusDescription + "\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
                    await stream.WriteAsync(header, 0, header.Length);
                    await stream.WriteAsync(body, 0, body.Length);
                }
                else
                {
                    string header = "HTTP/1.1 " + (int)response.StatusCode + " " + response.StatusDescription + "\r\nConnection: close\r\nContent-Type: " + response.ContentType + "\r\n";
                    if (response.ContentLength >= 0) header += "Content-Length: " + response.ContentLength + "\r\n";
                    // Keep redirected downloads inside the verified HTTPS bridge.
                    string location = response.Headers["Location"];
                    if (location != null)
                    {
                        Uri redirect = new Uri(destination, location);
                        if (IsDiscordDownload(redirect)) header += "Location: " + UpdaterBase + "files/" + Uri.EscapeDataString(redirect.AbsoluteUri) + "\r\n";
                    }
                    byte[] bytes = Encoding.ASCII.GetBytes(header + "\r\n");
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                    if (method != "HEAD") await response.GetResponseStream().CopyToAsync(stream);
                }
                Log("Guncelleme HTTPS -> " + destination.Host + " " + (int)response.StatusCode);
            }
        }
        static int SelfTest()
        {
            try
            {
                if (!AllowedDiscordTarget("discord.com", 443) || AllowedDiscordTarget("example.com", 443)) throw new IOException("Hedef filtresi hatali.");
                string pac = ProxyPac();
                if (!pac.Contains("PROXY 127.0.0.1:1088") || !pac.Contains("DIRECT") || pac.Contains("example.com")) throw new IOException("PAC filtresi hatali.");
                byte[] hello = BuildSelfTestClientHello("discord.com");
                byte[] split = SplitTlsClientHello(hello);
                if (split.Length != hello.Length + 5 || !VerifyTlsRecordPayload(hello, split)) throw new IOException("TLS kayit bolme testi basarisiz.");
                var random = new Random(15485863);
                for (int i = 0; i < 20000; i++)
                {
                    byte[] fuzz = new byte[random.Next(0, 513)]; random.NextBytes(fuzz);
                    int offset, length; TryFindSni(fuzz, out offset, out length);
                    SplitTlsClientHello(fuzz);
                }
                Console.WriteLine("[OK] Allowlist, PAC, TLS kayit butunlugu ve 20000 girdilik parser fuzz testi gecti.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("[HATA] " + ex.Message); return 1; }
        }

        static byte[] BuildSelfTestClientHello(string host)
        {
            byte[] name = Encoding.ASCII.GetBytes(host);
            var sni = new List<byte>();
            int listLength = 3 + name.Length;
            sni.Add((byte)(listLength >> 8)); sni.Add((byte)listLength); sni.Add(0);
            sni.Add((byte)(name.Length >> 8)); sni.Add((byte)name.Length); sni.AddRange(name);
            var extensions = new List<byte> { 0, 0, (byte)(sni.Count >> 8), (byte)sni.Count }; extensions.AddRange(sni);
            var body = new List<byte> { 3, 3 };
            for (int i = 0; i < 32; i++) body.Add((byte)i);
            body.Add(0); body.Add(0); body.Add(2); body.Add(0x13); body.Add(0x01); body.Add(1); body.Add(0);
            body.Add((byte)(extensions.Count >> 8)); body.Add((byte)extensions.Count); body.AddRange(extensions);
            var handshake = new List<byte> { 1, (byte)(body.Count >> 16), (byte)(body.Count >> 8), (byte)body.Count }; handshake.AddRange(body);
            var record = new List<byte> { 0x16, 3, 1, (byte)(handshake.Count >> 8), (byte)handshake.Count }; record.AddRange(handshake);
            return record.ToArray();
        }

        static bool VerifyTlsRecordPayload(byte[] original, byte[] transformed)
        {
            var joined = new List<byte>(); int p = 0;
            while (p < transformed.Length)
            {
                if (p + 5 > transformed.Length || transformed[p] != original[0] || transformed[p + 1] != original[1]) return false;
                int length = (transformed[p + 3] << 8) | transformed[p + 4]; p += 5;
                if (p + length > transformed.Length) return false;
                for (int i = 0; i < length; i++) joined.Add(transformed[p + i]);
                p += length;
            }
            if (joined.Count != original.Length - 5) return false;
            for (int i = 0; i < joined.Count; i++) if (joined[i] != original[i + 5]) return false;
            return true;
        }
    }
}
