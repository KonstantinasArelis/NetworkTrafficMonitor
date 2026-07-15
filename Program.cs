using System;
using System.Linq;
using System.Threading;
using System.Buffers.Binary;
using SharpPcap;

public class Program
{
    public static void Main(string[] args)
    {
        var devices = CaptureDeviceList.Instance;

        if (devices.Count < 1)
        {
            Console.WriteLine("No devices found");
            return;
        }

        // Console.WriteLine("Available devices found:");
        // foreach (ICaptureDevice device in devices)
        // {
        //     Console.WriteLine(device.ToString());
        // }

        ICaptureDevice myDevice = devices.First(d => d.Name == "enp5s0");
        myDevice.OnPacketArrival +=
            new SharpPcap.PacketArrivalEventHandler(device_OnPacketArrival);

        int readTimeoutMilliseconds = 1000;
        myDevice.Open(DeviceModes.Promiscuous, readTimeoutMilliseconds);

        myDevice.StartCapture();

        Console.WriteLine("Started listening2:");

        Thread.Sleep(5 * 1000);

        myDevice.StopCapture();

        ICaptureStatistics stats = myDevice.Statistics;

        Console.WriteLine("--- Capture Statistics ---");
        Console.WriteLine($"Total Packets Received by OS: {stats.ReceivedPackets}");
        Console.WriteLine($"Total Packets Dropped by OS Buffer: {stats.DroppedPackets}");
        Console.WriteLine($"Total Packets Dropped by NIC Hardware: {stats.InterfaceDroppedPackets}");

        myDevice.Close();
    }

    private static void device_OnPacketArrival(object sender, PacketCapture packet)
    {
        ReadOnlySpan<byte> L2_Frame = packet.Data;
        int L2_FrameLength = L2_Frame.Length;
        if (L2_FrameLength < 14)
        {
            return;
        }

        // LAYER 2
        ushort L2_EtherType = BinaryPrimitives.ReadUInt16BigEndian(L2_Frame.Slice(12, 2));
        if (L2_EtherType != 0x0800) // drop non ipv4 frames
        {
            return;
        }

        ulong L2_DestinationMAC = ReadMacAddress(L2_Frame.Slice(0, 6));
        ulong L2_SourceMAC = ReadMacAddress(L2_Frame.Slice(6, 6));

        // LAYER 3
        ushort ipTotalLength = BinaryPrimitives.ReadUInt16BigEndian(L2_Frame.Slice(16, 2));

        if (L2_Frame.Length < ipTotalLength + 14)
        {
            return; // ipTotalLength is invalid, even with padding
        }

        ReadOnlySpan<byte> ipv4Packet = L2_Frame.Slice(14, ipTotalLength);

        byte L3_Version = (byte)(ipv4Packet[0] >> 4);
        byte L3_IHL = (byte)(ipv4Packet[0] & 0x0F);
        byte L3_TOS = ipv4Packet[1];
        ushort L3_TotalLength = BinaryPrimitives.ReadUInt16BigEndian(ipv4Packet.Slice(2, 2));
        ushort L3_Identification = BinaryPrimitives.ReadUInt16BigEndian(ipv4Packet.Slice(4,2));
        byte L3_Flags = (byte)(ipv4Packet[6] >> 5);
        ushort L3_FragmentOffset = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(ipv4Packet.Slice(6,2)) & 0x1FFF);
        byte L3_TTL = ipv4Packet[8];
        byte L3_Protocol = ipv4Packet[9];
        ushort L3_HeaderChecksum = BinaryPrimitives.ReadUInt16BigEndian(ipv4Packet.Slice(10,2));
        uint L3_SourceIp = BinaryPrimitives.ReadUInt32BigEndian(ipv4Packet.Slice(12, 4));
        uint L3_DestIp = BinaryPrimitives.ReadUInt32BigEndian(ipv4Packet.Slice(16, 4));

        int ipHeaderLengthBytes = L3_IHL * 4;

        if (ipHeaderLengthBytes < 20 || ipHeaderLengthBytes > ipTotalLength) 
        {
            return; // Malformed packet
        }
        if (L3_Version != 4)
        {
            return; // We only want ipv4
        }

        // Fix: Read options safely using the corrected byte length
        if (ipHeaderLengthBytes > 20)
        {
            ReadOnlySpan<byte> L3_Options = ipv4Packet.Slice(20, ipHeaderLengthBytes - 20);
        }

        // Fix: Slice payload using the proper header byte length
        ReadOnlySpan<byte> L3_Payload = ipv4Packet.Slice(ipHeaderLengthBytes, ipTotalLength - ipHeaderLengthBytes); 

        // LAYER 4
        if (L3_Protocol != 6 && L3_Protocol != 17)
        {
            return; // only tcp or udp
        }

        ushort L4_SourcePort = BinaryPrimitives.ReadUInt16BigEndian(L3_Payload.Slice(0, 2));
        ushort L4_DestPort = BinaryPrimitives.ReadUInt16BigEndian(L3_Payload.Slice(2, 2));


        Console.WriteLine("frameLength={0} | EtherType=0x{1:X4} | dest MAC {2:X12} | source MAC {3:X12} | ipPayloadLength {4} | source {5}:{6} | dest {7}:{8} | {9}", 
            L2_FrameLength, L2_EtherType, L2_DestinationMAC, L2_SourceMAC, ipTotalLength, ToIpString(L3_SourceIp), L4_SourcePort, ToIpString(L3_DestIp), L4_DestPort, TcpOrUdpToString(L3_Protocol));
    }

    // mac is 6 byte, we place it in 8 bytes
    private static ulong ReadMacAddress(ReadOnlySpan<byte> bytes)
    {
        uint part1 = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(0, 4));
        uint part2 = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(4, 2));

        return ((ulong)part1 << 16) | part2;
    }

    public static string ToIpString(uint ipAddress)
    {
        // Shift bits to the right and cast to byte to drop the overflow
        byte octet1 = (byte)(ipAddress >> 24);
        byte octet2 = (byte)(ipAddress >> 16);
        byte octet3 = (byte)(ipAddress >> 8);
        byte octet4 = (byte)(ipAddress);

        // String interpolation handles the string allocation for the UI
        return $"{octet1}.{octet2}.{octet3}.{octet4}";
    }

    public static string TcpOrUdpToString(byte protocol)
    {
        switch (protocol)
        {
            case 6:
            return "TCP";
            case 17:
            return "UDP";
        }

        throw new Exception("Protocol is not tcp or udp. Not good.");
    }
}

// fix ports being parsed incorrectly.
// fix options throwing errors.

// why are subnet masks needed? (aside from network administration)
// switch to polling instead of event driven???
// System.Threading.Channels
// sql lite periodic flush
// sudo setcap cap_net_raw,cap_net_admin=eip ./bin/Debug/net10.0/TrafficAnalyzer