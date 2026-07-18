using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace NetCheck
{
    internal sealed class NetworkSnapshot
    {
        public string AdapterName = "";
        public string AdapterDescription = "";
        public string TypeCode = "Disconnected";
        public int WifiSignal = -1;

        public string Signature
        {
            get { return AdapterName + "|" + AdapterDescription + "|" + TypeCode + "|" + WifiSignal; }
        }

        public string TypeDisplay
        {
            get
            {
                if (TypeCode == "WiFi") return L.T("Wi-Fi（無線）", "Wi-Fi (Wireless)");
                if (TypeCode == "Wired") return L.T("有線網路", "Wired");
                if (TypeCode == "VPN") return "VPN";
                if (TypeCode == "Other") return L.T("其他", "Other");
                return L.T("未連線", "Disconnected");
            }
        }

        public string SignalDisplay
        {
            get { return TypeCode == "WiFi" ? (WifiSignal >= 0 ? WifiSignal + "%" : L.T("無法取得", "Unavailable")) : L.T("不適用", "Not applicable"); }
        }

        public string AdapterDisplay
        {
            get
            {
                if (String.IsNullOrEmpty(AdapterName)) return L.T("找不到使用中的網卡", "No active network adapter");
                if (String.IsNullOrEmpty(AdapterDescription) || String.Equals(AdapterName, AdapterDescription, StringComparison.OrdinalIgnoreCase)) return AdapterName;
                return AdapterName + " / " + AdapterDescription;
            }
        }

        public string UiText
        {
            get { return L.T("目前網卡：", "Adapter: ") + AdapterDisplay + L.T("｜連線類型：", " | Type: ") + TypeDisplay + L.T("｜Wi-Fi 訊號：", " | Wi-Fi signal: ") + SignalDisplay; }
        }

        public string ToMarker()
        {
            return "Adapter=" + Encode(AdapterName) + ";Description=" + Encode(AdapterDescription) + ";Type=" + TypeCode + ";Signal=" + WifiSignal;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }
    }

    internal static class NetworkStatusReader
    {
        private const int WlanClientVersion = 2;
        private const int CurrentConnectionOpcode = 7;

        [DllImport("wlanapi.dll")]
        private static extern int WlanOpenHandle(int clientVersion, IntPtr reserved, out int negotiatedVersion, out IntPtr clientHandle);

        [DllImport("wlanapi.dll")]
        private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        private static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

        [DllImport("wlanapi.dll")]
        private static extern int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opcode, IntPtr reserved, out int dataSize, out IntPtr data, out int opcodeValueType);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr memory);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WlanInterfaceInfo
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
            public int State;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Dot11Ssid
        {
            public uint Length;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WlanAssociationAttributes
        {
            public Dot11Ssid Ssid;
            public int BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
            public int PhyType;
            public uint PhyIndex;
            public uint SignalQuality;
            public uint RxRate;
            public uint TxRate;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WlanConnectionAttributes
        {
            public int State;
            public int ConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
            public WlanAssociationAttributes Association;
        }

        public static NetworkSnapshot Capture()
        {
            try
            {
                NetworkInterface adapter = FindPrimaryAdapter();
                if (adapter == null) return new NetworkSnapshot();
                var result = new NetworkSnapshot
                {
                    AdapterName = adapter.Name ?? "",
                    AdapterDescription = adapter.Description ?? "",
                    TypeCode = TypeCode(adapter.NetworkInterfaceType)
                };
                if (result.TypeCode == "WiFi")
                {
                    Guid id;
                    if (Guid.TryParse(adapter.Id, out id)) result.WifiSignal = ReadWifiSignal(id);
                }
                return result;
            }
            catch { return new NetworkSnapshot(); }
        }

        private static NetworkInterface FindPrimaryAdapter()
        {
            IPAddress routedAddress = FindRoutedAddress();
            NetworkInterface best = null;
            long bestScore = Int64.MinValue;
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (item.OperationalStatus != OperationalStatus.Up || item.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                long score = Math.Min(item.Speed / 1000000L, 1000L);
                IPInterfaceProperties properties;
                try { properties = item.GetIPProperties(); } catch { continue; }
                if (routedAddress != null)
                {
                    foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                        if (address.Address.Equals(routedAddress)) score += 100000;
                }
                foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                    if (gateway.Address != null && !gateway.Address.Equals(IPAddress.Any) && !gateway.Address.Equals(IPAddress.IPv6Any)) { score += 10000; break; }
                if (item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || IsEthernet(item.NetworkInterfaceType)) score += 100;
                if (score > bestScore) { bestScore = score; best = item; }
            }
            return best;
        }

        private static IPAddress FindRoutedAddress()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("1.1.1.1", 53);
                    var endpoint = socket.LocalEndPoint as IPEndPoint;
                    return endpoint == null ? null : endpoint.Address;
                }
            }
            catch { return null; }
        }

        private static bool IsEthernet(NetworkInterfaceType type)
        {
            return type == NetworkInterfaceType.Ethernet || type == NetworkInterfaceType.Ethernet3Megabit || type == NetworkInterfaceType.FastEthernetFx || type == NetworkInterfaceType.FastEthernetT || type == NetworkInterfaceType.GigabitEthernet;
        }

        private static string TypeCode(NetworkInterfaceType type)
        {
            if (type == NetworkInterfaceType.Wireless80211) return "WiFi";
            if (IsEthernet(type)) return "Wired";
            if (type == NetworkInterfaceType.Ppp || type == NetworkInterfaceType.Tunnel) return "VPN";
            return "Other";
        }

        private static int ReadWifiSignal(Guid target)
        {
            IntPtr handle = IntPtr.Zero;
            IntPtr list = IntPtr.Zero;
            try
            {
                int negotiated;
                if (WlanOpenHandle(WlanClientVersion, IntPtr.Zero, out negotiated, out handle) != 0) return -1;
                if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero) return -1;
                int count = Marshal.ReadInt32(list, 0);
                int offset = 8;
                int size = Marshal.SizeOf(typeof(WlanInterfaceInfo));
                for (int i = 0; i < count; i++)
                {
                    var info = (WlanInterfaceInfo)Marshal.PtrToStructure(IntPtr.Add(list, offset + i * size), typeof(WlanInterfaceInfo));
                    if (info.InterfaceGuid != target || info.State != 1) continue;
                    IntPtr data = IntPtr.Zero;
                    try
                    {
                        int dataSize, valueType;
                        Guid id = info.InterfaceGuid;
                        if (WlanQueryInterface(handle, ref id, CurrentConnectionOpcode, IntPtr.Zero, out dataSize, out data, out valueType) != 0 || data == IntPtr.Zero) return -1;
                        var connection = (WlanConnectionAttributes)Marshal.PtrToStructure(data, typeof(WlanConnectionAttributes));
                        return (int)Math.Min(100U, connection.Association.SignalQuality);
                    }
                    finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
                }
            }
            catch { }
            finally
            {
                if (list != IntPtr.Zero) WlanFreeMemory(list);
                if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
            }
            return -1;
        }
    }
}
