using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetCheck
{
    internal sealed class AdvancedDiagnosticResult
    {
        public bool InterfaceDetected;
        public string Gateway = "";
        public int GatewayReachable = -1;
        public int DnsResolved = -1;
        public int Ipv4External = -1;
        public int Ipv6External = -1;
        public int HttpsTarget = 0;
        public int WifiSignal = -1;
        public readonly List<string> FindingCodes = new List<string>();

        public string ToLogString()
        {
            return "DIAG|Findings=" + String.Join("+", FindingCodes.ToArray())
                + ";Interface=" + (InterfaceDetected ? "1" : "0")
                + ";Gateway=" + (String.IsNullOrEmpty(Gateway) ? "none" : Gateway)
                + ";GatewayReachable=" + GatewayReachable
                + ";DNS=" + DnsResolved
                + ";IPv4=" + Ipv4External
                + ";IPv6=" + Ipv6External
                + ";HTTPS=" + HttpsTarget
                + ";WiFi=" + WifiSignal;
        }

        public string DisplaySummary
        {
            get
            {
                var labels = new List<string>();
                foreach (string code in FindingCodes) labels.Add(FindingLabel(code));
                string findings = labels.Count == 0 ? L.T("無法判定故障層級", "Unable to determine the failure layer") : String.Join(L.T("、", ", "), labels.ToArray());
                return findings + L.T("；介面=", "; interface=") + YesNo(InterfaceDetected ? 1 : 0)
                    + L.T("，閘道=", ", gateway=") + (String.IsNullOrEmpty(Gateway) ? L.T("無", "none") : Gateway + " / " + YesNo(GatewayReachable))
                    + ", DNS=" + YesNo(DnsResolved) + ", IPv4=" + YesNo(Ipv4External) + ", IPv6=" + YesNo(Ipv6External)
                    + ", HTTPS=" + YesNo(HttpsTarget) + L.T("，Wi-Fi 訊號=", ", Wi-Fi signal=") + (WifiSignal < 0 ? L.T("不適用／無法取得", "N/A or unavailable") : WifiSignal + "%");
            }
        }

        internal static string FindingsFromLog(string detail)
        {
            int start = (detail ?? "").IndexOf("DIAG|Findings=", StringComparison.Ordinal);
            if (start < 0) return "";
            start += "DIAG|Findings=".Length;
            int end = detail.IndexOf(';', start);
            string value = end < 0 ? detail.Substring(start) : detail.Substring(start, end - start);
            var labels = new List<string>();
            foreach (string code in value.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries)) labels.Add(FindingLabel(code));
            return String.Join(L.T("、", ", "), labels.ToArray());
        }

        internal static string EvidenceFromLog(string detail)
        {
            int start = (detail ?? "").IndexOf("DIAG|", StringComparison.Ordinal);
            if (start < 0) return L.T("未執行進階診斷", "Advanced diagnostics not performed");
            return detail.Substring(start + 5).Replace(';', ' ');
        }

        internal static string FindingLabel(string code)
        {
            if (code == "NO_INTERFACE") return L.T("網路線或網卡中斷", "Network cable or adapter disconnected");
            if (code == "WEAK_WIFI") return L.T("Wi-Fi 訊號過弱", "Wi-Fi signal too weak");
            if (code == "GATEWAY_UNREACHABLE") return L.T("路由器／閘道無回應", "Router/default gateway not responding");
            if (code == "DNS_FAILED") return L.T("DNS 解析失敗", "DNS resolution failed");
            if (code == "IPV4_FAILED") return L.T("IPv4 失敗但 IPv6 正常", "IPv4 failed but IPv6 is working");
            if (code == "IPV6_FAILED") return L.T("IPv6 失敗但 IPv4 正常", "IPv6 failed but IPv4 is working");
            if (code == "ISP_OUTAGE") return L.T("ISP 對外網路可能中斷", "Possible ISP Internet outage");
            if (code == "TARGET_ONLY") return L.T("只有特定測試網站失敗", "Only the configured test website(s) failed");
            return code;
        }

        private static string YesNo(int value)
        {
            if (value < 0) return L.T("未取得", "unknown");
            return value == 1 ? L.T("正常", "OK") : L.T("失敗", "failed");
        }
    }

    internal static class AdvancedNetworkDiagnostics
    {
        internal static AdvancedDiagnosticResult Run(NetworkSnapshot snapshot, string[] targets)
        {
            var result = new AdvancedDiagnosticResult();
            result.WifiSignal = snapshot == null ? -1 : snapshot.WifiSignal;
            NetworkInterface adapter = FindAdapter(snapshot);
            result.InterfaceDetected = adapter != null && NetworkInterface.GetIsNetworkAvailable();
            if (!result.InterfaceDetected)
            {
                result.FindingCodes.Add("NO_INTERFACE");
                return result;
            }

            IPAddress gateway = FindGateway(adapter);
            result.Gateway = gateway == null ? "" : gateway.ToString();
            result.GatewayReachable = gateway == null ? -1 : (PingAddress(gateway, 1500) ? 1 : 0);
            string host = FirstHost(targets);
            result.DnsResolved = ResolveHost(host, 3000) ? 1 : 0;
            result.Ipv4External = CanConnect(IPAddress.Parse("1.1.1.1"), 443, 2500) ? 1 : 0;
            result.Ipv6External = Socket.OSSupportsIPv6 && CanConnect(IPAddress.Parse("2606:4700:4700::1111"), 443, 2500) ? 1 : 0;

            AddFindings(result, snapshot);
            return result;
        }

        internal static bool RunClassificationSelfTest()
        {
            var noInterface = new AdvancedDiagnosticResult { InterfaceDetected = false };
            AddFindings(noInterface, new NetworkSnapshot());
            var dns = new AdvancedDiagnosticResult { InterfaceDetected = true, GatewayReachable = 1, DnsResolved = 0, Ipv4External = 1, Ipv6External = 0, HttpsTarget = 0 };
            AddFindings(dns, new NetworkSnapshot { TypeCode = "Wired" });
            var weak = new AdvancedDiagnosticResult { InterfaceDetected = true, GatewayReachable = 1, DnsResolved = 1, Ipv4External = 1, Ipv6External = 1, HttpsTarget = 0, WifiSignal = 18 };
            AddFindings(weak, new NetworkSnapshot { TypeCode = "WiFi", WifiSignal = 18 });
            return noInterface.FindingCodes.Contains("NO_INTERFACE")
                && dns.FindingCodes.Contains("DNS_FAILED") && dns.FindingCodes.Contains("IPV6_FAILED") && !dns.FindingCodes.Contains("TARGET_ONLY")
                && weak.FindingCodes.Contains("WEAK_WIFI") && weak.FindingCodes.Contains("TARGET_ONLY")
                && AdvancedDiagnosticResult.FindingsFromLog(weak.ToLogString()).Contains(L.T("Wi-Fi 訊號過弱", "Wi-Fi signal too weak"));
        }

        private static void AddFindings(AdvancedDiagnosticResult result, NetworkSnapshot snapshot)
        {
            if (!result.InterfaceDetected) { Add(result, "NO_INTERFACE"); return; }
            if (snapshot != null && snapshot.TypeCode == "WiFi" && result.WifiSignal >= 0 && result.WifiSignal < 25) Add(result, "WEAK_WIFI");
            if (result.GatewayReachable == 0 && result.Ipv4External == 0 && result.Ipv6External == 0) Add(result, "GATEWAY_UNREACHABLE");
            if (result.DnsResolved == 0) Add(result, "DNS_FAILED");
            if (result.Ipv4External == 0 && result.Ipv6External == 1) Add(result, "IPV4_FAILED");
            if (result.Ipv6External == 0 && result.Ipv4External == 1) Add(result, "IPV6_FAILED");
            if (result.Ipv4External == 0 && result.Ipv6External == 0) Add(result, "ISP_OUTAGE");
            if (result.DnsResolved == 1 && (result.Ipv4External == 1 || result.Ipv6External == 1) && result.HttpsTarget == 0) Add(result, "TARGET_ONLY");
        }

        private static void Add(AdvancedDiagnosticResult result, string code)
        {
            if (!result.FindingCodes.Contains(code)) result.FindingCodes.Add(code);
        }

        private static NetworkInterface FindAdapter(NetworkSnapshot snapshot)
        {
            try
            {
                NetworkInterface fallback = null;
                foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (item.OperationalStatus != OperationalStatus.Up || item.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (snapshot != null && (String.Equals(item.Name, snapshot.AdapterName, StringComparison.OrdinalIgnoreCase) || String.Equals(item.Description, snapshot.AdapterDescription, StringComparison.OrdinalIgnoreCase))) return item;
                    if (fallback == null) fallback = item;
                }
                return fallback;
            }
            catch { return null; }
        }

        private static IPAddress FindGateway(NetworkInterface adapter)
        {
            try
            {
                foreach (GatewayIPAddressInformation item in adapter.GetIPProperties().GatewayAddresses)
                    if (item.Address != null && !item.Address.Equals(IPAddress.Any) && !item.Address.Equals(IPAddress.IPv6Any)) return item.Address;
            }
            catch { }
            return null;
        }

        private static bool PingAddress(IPAddress address, int timeout)
        {
            try { using (var ping = new Ping()) return ping.Send(address, timeout).Status == IPStatus.Success; }
            catch { return false; }
        }

        private static string FirstHost(string[] targets)
        {
            if (targets != null) foreach (string target in targets) { Uri uri; if (Uri.TryCreate(target, UriKind.Absolute, out uri) && !String.IsNullOrEmpty(uri.Host)) return uri.Host; }
            return "www.microsoft.com";
        }

        private static bool ResolveHost(string host, int timeout)
        {
            try
            {
                IAsyncResult operation = Dns.BeginGetHostAddresses(host, null, null);
                if (!operation.AsyncWaitHandle.WaitOne(timeout)) return false;
                IPAddress[] addresses = Dns.EndGetHostAddresses(operation);
                return addresses != null && addresses.Length > 0;
            }
            catch { return false; }
        }

        private static bool CanConnect(IPAddress address, int port, int timeout)
        {
            using (var client = new TcpClient(address.AddressFamily))
            {
                try
                {
                    IAsyncResult operation = client.BeginConnect(address, port, null, null);
                    if (!operation.AsyncWaitHandle.WaitOne(timeout)) return false;
                    client.EndConnect(operation);
                    return client.Connected;
                }
                catch { return false; }
            }
        }
    }
}
