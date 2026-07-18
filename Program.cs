using System;
using System.Linq;
using System.Threading;
using System.Buffers.Binary;
using SharpPcap;
using PacketDotNet;
using System.Threading.Channels;
using System.Net.WebSockets;
using System.Text.Json;
using System.Net;

public class Program
{
    private static readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public static void Main(string[] args)
    {
        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("\n[!] Ctrl+C detected. Shutting down gracefully...");
            e.Cancel = true;
            _cts.Cancel();
        };

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
        //ICaptureDevice myDevice = devices.First(d => d.Name == "lo");

        int readTimeoutMilliseconds = 100;
        myDevice.Open(DeviceModes.Promiscuous, readTimeoutMilliseconds);

        PacketCapture packet;
        GetPacketStatus status;
        Channel<ParsedPacket> channel = Channel.CreateUnbounded<ParsedPacket>();
        Task consumer = ConsumeAsync(channel.Reader, _cts.Token);

        while (!_cts.Token.IsCancellationRequested)
        {
            status = myDevice.GetNextPacket(out packet);

            if (status == GetPacketStatus.PacketRead)
            {
                device_OnPacketArrival(packet: packet, writer: channel.Writer);
            }
            else if (status == GetPacketStatus.ReadTimeout)
            {
                continue;
            }
            else if (status == GetPacketStatus.Error)
            {
                Console.WriteLine("Something went wrong");
                break;
            }
        }

        ICaptureStatistics stats = myDevice.Statistics;

        Console.WriteLine("--- Capture Statistics ---");
        Console.WriteLine($"Total Packets Received by OS: {stats.ReceivedPackets}");
        Console.WriteLine($"Total Packets Dropped by OS Buffer: {stats.DroppedPackets}");
        Console.WriteLine($"Total Packets Dropped by NIC Hardware: {stats.InterfaceDroppedPackets}");

        myDevice.Close();
    }

    private static async Task ConsumeAsync(ChannelReader<ParsedPacket> reader, CancellationToken token)
    {
        using HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8000/packets/");
        listener.Start();
        Console.WriteLine("C# WebSocket Server running! Waiting for browser on ws://localhost:8000/packets/");

        JsonSerializerOptions jsonOptions = new JsonSerializerOptions 
        { 
            IncludeFields = true 
        };

        try
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext httpListenerContext = await listener.GetContextAsync();

                if (!httpListenerContext.Request.IsWebSocketRequest)
                {
                    httpListenerContext.Response.StatusCode = 400;
                    httpListenerContext.Response.Close();
                    continue;
                }

                HttpListenerWebSocketContext webSocketContext = await httpListenerContext.AcceptWebSocketAsync(null);
                using WebSocket ws = webSocketContext.WebSocket;
                Console.WriteLine("Browser connected! Streaming packets...");

                try
                {
                    await foreach (ParsedPacket item in reader.ReadAllAsync(token))
                    {
                        Console.WriteLine(item.ToString());

                        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(item, jsonOptions);

                        await ws.SendAsync(
                            buffer: jsonBytes,
                            messageType: WebSocketMessageType.Text,
                            endOfMessage: true,
                            cancellationToken: token);
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("Browser disconnected. Waiting for a new connection...");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Server shutting down.");        
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void device_OnPacketArrival(PacketCapture packet, object sender = null, ChannelWriter<ParsedPacket> writer = null)
    {
        // Preamble, SFD, CRC are already stripped from packet
        ReadOnlySpan<byte> L2_Frame = packet.Data;
        int L2_FrameLength = L2_Frame.Length;
        if (L2_FrameLength < 14)
        {
            return;
        }

        // LAYER 2
        ushort L2_EtherType = BinaryPrimitives.ReadUInt16BigEndian(L2_Frame.Slice(12, 2));
        if (L2_EtherType < 0x0600)
        {
            return; // ethernet 1 packet, which uses this header for size, not for frame type
        }

        ulong L2_DestinationMAC = ReadMacAddress(L2_Frame.Slice(0, 6));
        ulong L2_SourceMAC = ReadMacAddress(L2_Frame.Slice(6, 6));

        if (L2_EtherType != 0x0800) 
        {
            ParsedPacket nonIpPacket = new ParsedPacket(
                sourceMac: L2_SourceMAC,
                destMac: L2_DestinationMAC,
                etherType: L2_EtherType
            );
            
            writer.TryWrite(nonIpPacket);
            return;
        }

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
            Console.WriteLine($"Skipped packet, because L3_Version is not ipv4, its {L3_Version : X4}");
            return; // We only want ipv4
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
                sourceMac: L2_SourceMAC,
                destMac: L2_DestinationMAC,
                etherType: L2_EtherType,
                sourceIp: L3_SourceIp,
                destIp: L3_DestIp,
                networkProtocol: L3_Protocol
            );
            
            writer.TryWrite(ipButNotTcpUdpPacket);
            return;
        }

        ushort L4_SourcePort = BinaryPrimitives.ReadUInt16BigEndian(L3_Payload.Slice(0, 2));
        ushort L4_DestPort = BinaryPrimitives.ReadUInt16BigEndian(L3_Payload.Slice(2, 2));

        ParsedPacket fullTcpUdpPacket = new ParsedPacket(
            sourceMac: L2_SourceMAC,
            destMac: L2_DestinationMAC,
            etherType: L2_EtherType,
            sourceIp: L3_SourceIp,
            destIp: L3_DestIp,
            networkProtocol: L3_Protocol,
            sourcePort: L4_SourcePort,
            destPort: L4_DestPort
        );
        
        writer.TryWrite(fullTcpUdpPacket);
        //Console.WriteLine("frameLength={0} | EtherType=0x{1:X4} | dest MAC {2:X12} | source MAC {3:X12} | ipPayloadLength {4} | source {5}:{6} | dest {7}:{8} | {9}", 
        //    L2_FrameLength, L2_EtherType, L2_DestinationMAC, L2_SourceMAC, ipTotalLength, ToIpString(L3_SourceIp), L4_SourcePort, ToIpString(L3_DestIp), L4_DestPort, TcpOrUdpToString(L3_Protocol));
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

    readonly struct ParsedPacket
    {
        public readonly ulong sourceMac;
        public readonly ulong destMac;
        public readonly ushort etherType;

        public readonly uint? sourceIp;
        public readonly uint? destIp;
        public readonly ushort? sourcePort;
        public readonly ushort? destPort;
        public readonly byte? networkProtocol;

        public ParsedPacket(
            ulong sourceMac, 
            ulong destMac, 
            ushort etherType, 
            uint? sourceIp = null, 
            uint? destIp = null, 
            byte? networkProtocol = null,
            ushort? sourcePort = null, 
            ushort? destPort = null)
        {
            this.sourceMac = sourceMac;
            this.destMac = destMac;
            this.etherType = etherType;
            this.sourceIp = sourceIp;
            this.destIp = destIp;
            this.networkProtocol = networkProtocol;
            this.sourcePort = sourcePort;
            this.destPort = destPort;
        }

        public override string ToString()
        {
            // Safely format optional IPs
            string srcIpStr = sourceIp.HasValue ? Program.ToIpString(sourceIp.Value) : "N/A";
            string dstIpStr = destIp.HasValue ? Program.ToIpString(destIp.Value) : "N/A";
            
            // Safely format optional Ports
            string srcPortStr = sourcePort.HasValue ? sourcePort.Value.ToString() : "N/A";
            string dstPortStr = destPort.HasValue ? destPort.Value.ToString() : "N/A";

            // Safely format Protocol
            string protocolStr = "N/A";
            if (networkProtocol.HasValue)
            {
                protocolStr = networkProtocol.Value switch
                {
                    6 => "TCP",
                    17 => "UDP",
                    _ => $"Other ({networkProtocol.Value})"
                };
            }

            return $"EtherType=0x{etherType:X4} | dest MAC {destMac:X12} | source MAC {sourceMac:X12} | source {srcIpStr}:{srcPortStr} | dest {dstIpStr}:{dstPortStr} | {protocolStr}";
        }
    }
}

// why are subnet masks needed? (aside from network administration)
// sql lite periodic flush