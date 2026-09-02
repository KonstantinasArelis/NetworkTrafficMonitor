using System.Buffers.Binary;
using SharpPcap;
using TrafficAnalyzer.Core;

namespace TrafficAnalyzer.Parser;

public class Parser
{
    public static ParsedPacket? ParsePacket(PacketCapture packet)
    {
        //PrintHexDump(packet.Data);

        DateTime packetTimestamp = packet.Header.Timeval.Date;
        // Preamble, SFD, CRC are already stripped from packet
        ReadOnlySpan<byte> L2_Frame = packet.Data;
        int L2_FrameLength = L2_Frame.Length;
        if (L2_FrameLength < 14)
        {
            return null;
        }

        // LAYER 2
        ushort L2_EtherType = BinaryPrimitives.ReadUInt16BigEndian(L2_Frame.Slice(12, 2));
        if (L2_EtherType < 0x0600)
        {
            return null; // ethernet 1 packet, which uses this header for size, not for frame type
        }

        ulong L2_DestinationMAC = Helpers.ReadMacAddress(L2_Frame.Slice(0, 6));
        ulong L2_SourceMAC = Helpers.ReadMacAddress(L2_Frame.Slice(6, 6));

        if (L2_EtherType != 0x0800) 
        {
            ParsedPacket nonIpPacket = new ParsedPacket(
                captureTime: packetTimestamp,
                sourceMac: L2_SourceMAC,
                destMac: L2_DestinationMAC,
                etherType: L2_EtherType
            );
            
            return nonIpPacket;
        }

        // LAYER 3
        ushort ipTotalLength = BinaryPrimitives.ReadUInt16BigEndian(L2_Frame.Slice(16, 2));
        if (ipTotalLength == 0)
        {
            // The IP packet length is the total captured frame minus the 14-byte Ethernet header
            ipTotalLength = (ushort)(L2_Frame.Length - 14);
        }

        if (L2_Frame.Length < ipTotalLength + 14)
        {
            return null; // ipTotalLength is invalid, even with padding
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
            return null; // Malformed packet
        }
        if (L3_Version != 4)
        {
            Console.WriteLine($"Skipped packet, because L3_Version is not ipv4, its {L3_Version : X4}");
            return null; // We only want ipv4
        }

        if (ipHeaderLengthBytes > 20)
        {
            ReadOnlySpan<byte> L3_Options = ipv4Packet.Slice(20, ipHeaderLengthBytes - 20);
        }

        ReadOnlySpan<byte> L3_Payload = ipv4Packet.Slice(ipHeaderLengthBytes, ipTotalLength - ipHeaderLengthBytes); 

        // LAYER 4
        if (L3_Protocol != 6 && L3_Protocol != 17)
        {
            ParsedPacket ipButNotTcpUdpPacket = new ParsedPacket(
                captureTime: packetTimestamp,
                sourceMac: L2_SourceMAC,
                destMac: L2_DestinationMAC,
                etherType: L2_EtherType,
                sourceIp: L3_SourceIp,
                destIp: L3_DestIp,
                networkProtocol: L3_Protocol
            );
            
            return ipButNotTcpUdpPacket;
        }

        ushort L4_SourcePort = BinaryPrimitives.ReadUInt16BigEndian(L3_Payload.Slice(0, 2));
        ushort L4_DestPort = BinaryPrimitives.ReadUInt16BigEndian(L3_Payload.Slice(2, 2));

        ParsedPacket fullTcpUdpPacket = new ParsedPacket(
            captureTime: packetTimestamp,
            sourceMac: L2_SourceMAC,
            destMac: L2_DestinationMAC,
            etherType: L2_EtherType,
            sourceIp: L3_SourceIp,
            destIp: L3_DestIp,
            networkProtocol: L3_Protocol,
            sourcePort: L4_SourcePort,
            destPort: L4_DestPort
        );
        
        return fullTcpUdpPacket;
        //Console.WriteLine("frameLength={0} | EtherType=0x{1:X4} | dest MAC {2:X12} | source MAC {3:X12} | ipPayloadLength {4} | source {5}:{6} | dest {7}:{8} | {9}", 
        //    L2_FrameLength, L2_EtherType, L2_DestinationMAC, L2_SourceMAC, ipTotalLength, ToIpString(L3_SourceIp), L4_SourcePort, ToIpString(L3_DestIp), L4_DestPort, TcpOrUdpToString(L3_Protocol));
    }

    private static void PrintHexDump(ReadOnlySpan<byte> data)
    {
        int length = Math.Min(data.Length, 64); // Only print the headers to avoid terminal spam
        Console.WriteLine($"\n--- Packet Captured ({data.Length} bytes total) ---");

        for (int i = 0; i < length; i += 16)
        {
            Console.Write($"{i:X4}  "); // Print offset (e.g., 0000, 0010)

            // Print hex values
            for (int j = 0; j < 16; j++)
            {
                if (i + j < length) Console.Write($"{data[i + j]:X2} ");
                else Console.Write("   ");
            }

            Console.Write(" ");

            // Print readable ASCII characters
            for (int j = 0; j < 16; j++)
            {
                if (i + j < length)
                {
                    char c = (char)data[i + j];
                    Console.Write(char.IsControl(c) || c > 127 ? '.' : c);
                }
            }
            Console.WriteLine();
        }
    }
}