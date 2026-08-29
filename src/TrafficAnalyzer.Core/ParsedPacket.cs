namespace TrafficAnalyzer.Core;

public readonly struct ParsedPacket
{
    public readonly DateTime captureTime;
    public readonly ulong sourceMac;
    public readonly ulong destMac;
    public readonly ushort etherType;

    public readonly uint? sourceIp;
    public readonly uint? destIp;
    public readonly ushort? sourcePort;
    public readonly ushort? destPort;
    public readonly byte? networkProtocol;

    public ParsedPacket(
        DateTime captureTime,
        ulong sourceMac, 
        ulong destMac, 
        ushort etherType, 
        uint? sourceIp = null, 
        uint? destIp = null, 
        byte? networkProtocol = null,
        ushort? sourcePort = null, 
        ushort? destPort = null)
    {
        this.captureTime = captureTime;
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
        string srcIpStr = sourceIp.HasValue ? Helpers.ToIpString(sourceIp.Value) : "N/A";
        string dstIpStr = destIp.HasValue ? Helpers.ToIpString(destIp.Value) : "N/A";
        
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
