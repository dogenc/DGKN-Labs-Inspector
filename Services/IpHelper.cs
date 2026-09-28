using System.Net;
using System.Runtime.InteropServices;

namespace DGKNInspector.Services;

/// <summary>
/// Liest aktive TCP-/UDP-Verbindungen samt Besitzer-PID direkt aus der
/// Windows IP Helper API (iphlpapi.dll) — dieselbe Quelle wie "netstat -ano",
/// aber ohne einen externen Prozess zu starten.
/// </summary>
public static class IpHelper
{
    public enum Proto { TcpV4, TcpV6, UdpV4, UdpV6 }

    public sealed record Connection(
        Proto Protocol,
        IPAddress LocalAddress,
        int LocalPort,
        IPAddress? RemoteAddress,
        int RemotePort,
        string State,
        int Pid);

    // --- TCP ---
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int UDP_TABLE_OWNER_PID = 1;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen,
        bool sort, int ipVersion, int tblClass, int reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int dwOutBufLen,
        bool sort, int ipVersion, int tblClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;   // im Netzwerk-Byteorder, nur untere 2 Bytes
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] localAddr;
        public uint localScopeId;
        public uint localPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] remoteAddr;
        public uint remoteScopeId;
        public uint remotePort;
        public uint state;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint localAddr;
        public uint localPort;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] localAddr;
        public uint localScopeId;
        public uint localPort;
        public uint owningPid;
    }

    private static readonly string[] TcpStates =
    {
        "", "Geschlossen", "Lauscht", "SYN gesendet", "SYN empfangen",
        "Verbunden", "FIN Wait 1", "FIN Wait 2", "Close Wait",
        "Closing", "Last Ack", "Time Wait", "Delete TCB"
    };

    private static string StateName(uint s) => s < TcpStates.Length ? TcpStates[s] : s.ToString();

    private static int Port(uint netPort)
    {
        // Port liegt in den unteren zwei Bytes im Network-Byteorder.
        return ((int)(netPort & 0xFF) << 8) | (int)((netPort >> 8) & 0xFF);
    }

    public static List<Connection> GetAllConnections()
    {
        var list = new List<Connection>();
        list.AddRange(GetTcpV4());
        list.AddRange(GetTcpV6());
        list.AddRange(GetUdpV4());
        list.AddRange(GetUdpV6());
        return list;
    }

    private static IEnumerable<Connection> GetTcpV4()
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                yield break;

            int count = Marshal.ReadInt32(buf);
            IntPtr row = buf + 4;
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var r = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(row);
                row += rowSize;
                yield return new Connection(
                    Proto.TcpV4,
                    new IPAddress(r.localAddr), Port(r.localPort),
                    new IPAddress(r.remoteAddr), Port(r.remotePort),
                    StateName(r.state), (int)r.owningPid);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static IEnumerable<Connection> GetTcpV6()
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, true, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) yield break;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, true, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                yield break;

            int count = Marshal.ReadInt32(buf);
            IntPtr row = buf + 4;
            int rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var r = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(row);
                row += rowSize;
                yield return new Connection(
                    Proto.TcpV6,
                    new IPAddress(r.localAddr), Port(r.localPort),
                    new IPAddress(r.remoteAddr), Port(r.remotePort),
                    StateName(r.state), (int)r.owningPid);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static IEnumerable<Connection> GetUdpV4()
    {
        int size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, true, AF_INET, UDP_TABLE_OWNER_PID, 0);
        if (size == 0) yield break;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(buf, ref size, true, AF_INET, UDP_TABLE_OWNER_PID, 0) != 0)
                yield break;

            int count = Marshal.ReadInt32(buf);
            IntPtr row = buf + 4;
            int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var r = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(row);
                row += rowSize;
                yield return new Connection(
                    Proto.UdpV4,
                    new IPAddress(r.localAddr), Port(r.localPort),
                    null, 0, "—", (int)r.owningPid);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static IEnumerable<Connection> GetUdpV6()
    {
        int size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, true, AF_INET6, UDP_TABLE_OWNER_PID, 0);
        if (size == 0) yield break;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(buf, ref size, true, AF_INET6, UDP_TABLE_OWNER_PID, 0) != 0)
                yield break;

            int count = Marshal.ReadInt32(buf);
            IntPtr row = buf + 4;
            int rowSize = Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var r = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(row);
                row += rowSize;
                yield return new Connection(
                    Proto.UdpV6,
                    new IPAddress(r.localAddr), Port(r.localPort),
                    null, 0, "—", (int)r.owningPid);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
