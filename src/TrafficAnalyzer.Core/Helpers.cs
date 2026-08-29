using System.Buffers.Binary;

namespace TrafficAnalyzer.Core;

public class Helpers
{
    // mac is 6 byte, we place it in 8 bytes
    public static ulong ReadMacAddress(ReadOnlySpan<byte> bytes)
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