using System.Diagnostics;
using System.Threading.Channels;
using SharpPcap;
using TrafficAnalyzer.Core;
using PacketParser = TrafficAnalyzer.Parser.Parser;

namespace TrafficAnalyzer.Capture;

public interface ICapture
{
    Task StartCapture(ChannelWriter<ParsedPacket> writer, CancellationTokenSource _cts);
}

public class Capture : ICapture
{
    public async Task StartCapture(ChannelWriter<ParsedPacket> writer, CancellationTokenSource _cts)
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

        string deviceName = GetDeviceName();
        ICaptureDevice myDevice = devices.First(d => d.Name == deviceName);

        int readTimeoutMilliseconds = 1;
        myDevice.Open(DeviceModes.Promiscuous, readTimeoutMilliseconds);

        PacketCapture packet;
        GetPacketStatus status;
        //Channel<ParsedPacket> channel = Channel.CreateUnbounded<ParsedPacket>();
        //Task consumer = ConsumeAsync(channel.Reader, _cts.Token);

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

        while (!_cts.Token.IsCancellationRequested)
        {
            status = myDevice.GetNextPacket(out packet);

            if (status == GetPacketStatus.PacketRead)
            {
                ParsedPacket? parsedPacket = PacketParser.ParsePacket(packet);

                if (parsedPacket != null)
                {
                    writer.TryWrite(parsedPacket.Value);
                }
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

        TimeSpan ts = stopwatch.Elapsed;
        stopwatch.Stop();
        string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
            ts.Hours, ts.Minutes, ts.Seconds,
            ts.Milliseconds / 10);

        Console.WriteLine("--- Capture Statistics ---");
        Console.WriteLine("Monitoring time: " + elapsedTime);

        if (myDevice.Statistics != null)
        {
            ICaptureStatistics stats = myDevice.Statistics;

            Console.WriteLine($"Average packets / second: ${stats.ReceivedPackets / ts.TotalSeconds:N0}");
            Console.WriteLine($"Total Packets Received by OS: {stats.ReceivedPackets}");
            Console.WriteLine($"Total Packets Dropped by OS Buffer: {stats.DroppedPackets}");
            Console.WriteLine($"Total Packets Dropped by NIC Hardware: {stats.InterfaceDroppedPackets}");
        } else
        {
            Console.WriteLine("Other statistics unavailable");
        }


        myDevice.Close();
        writer.Complete();
        //await consumer;
    }

    private static string GetDeviceName()
    {
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows not supported. Exiting...");
            throw new NotImplementedException();
        } 
        else if (OperatingSystem.IsLinux())
        {
            return "lo";
            //return "enp5s0";
        } 
        else
        {
            Console.WriteLine("Operating system not supported. Exiting...");
            throw new NotImplementedException();
        }
    }
}