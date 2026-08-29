using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using TrafficAnalyzer.Core;
using PacketCapture = TrafficAnalyzer.Capture.Capture;

namespace TrafficAnalyzer.Client;

public class Program()
{
    private static readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public static async Task Main()
    {   
        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("\n[!] Cancelation detected. Shutting down gracefully...");
            e.Cancel = true;
            _cts.Cancel();
        };

        Channel<ParsedPacket> channel = Channel.CreateUnbounded<ParsedPacket>();

        // read channel
        Task consumer = ConsumeAsync(channel.Reader, _cts.Token);

        // write to channel
        var capture = new PacketCapture();
        await capture.StartCapture(channel.Writer, _cts);

        await consumer;
    }

    private static async Task ConsumeAsync(ChannelReader<ParsedPacket> reader, CancellationToken manualCancelationToken)
    {
        using HttpListener listener = new HttpListener();
        using CancellationTokenRegistration registration = manualCancelationToken.Register(() => listener.Abort() );
        listener.Prefixes.Add("http://localhost:8000/packets/");
        listener.Start();
        Console.WriteLine("C# WebSocket Server running! Waiting for browser on ws://localhost:8000/packets/");

        JsonSerializerOptions jsonOptions = new JsonSerializerOptions 
        { 
            IncludeFields = true 
        };

        List<int> batchSizes = new List<int>();
        try
        {
            while (!manualCancelationToken.IsCancellationRequested)
            {
                HttpListenerContext httpListenerContext;
                try
                {
                    httpListenerContext = await listener.GetContextAsync();
                } 
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (!httpListenerContext.Request.IsWebSocketRequest)
                {
                    httpListenerContext.Response.StatusCode = 400;
                    httpListenerContext.Response.Close();
                    continue;
                }

                HttpListenerWebSocketContext webSocketContext = await httpListenerContext.AcceptWebSocketAsync(null);
                using WebSocket ws = webSocketContext.WebSocket;
                Console.WriteLine("Browser connected! Streaming packets...");

                int maxBatchSize = 5000;
                TimeSpan maxWaitTime = TimeSpan.FromMilliseconds(16); // ~60 fps
                List<ParsedPacket> batch = new List<ParsedPacket>();

                while (!manualCancelationToken.IsCancellationRequested)
                {
                    using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(manualCancelationToken);
                    cts.CancelAfter(maxWaitTime);
                    CancellationToken CombinedCancelationToken = cts.Token; 

                    try
                    {
                        while (await reader.WaitToReadAsync(CombinedCancelationToken))
                        {
                            while (batch.Count < maxBatchSize && reader.TryRead(out var packet))
                            {
                                batch.Add(packet);
                            }

                            if (batch.Count >= maxBatchSize)
                            {
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {

                    }

                    if (batch.Count > 0)
                    {
                        batchSizes.Add(batch.Count);
                        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(batch, jsonOptions);
                        
                        try
                        {
                            await ws.SendAsync(
                                buffer: jsonBytes,
                                messageType: WebSocketMessageType.Text,
                                endOfMessage: true,
                                cancellationToken: manualCancelationToken);

                            batch.Clear();   
                        }
                        catch (WebSocketException)
                        {
                            Console.WriteLine("Browser disconnected. Waiting for a new connection...");
                            break;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Server shutting down.");        
        }
        finally
        {
            if (batchSizes.Count > 0)
            {
                Console.WriteLine($"Average batch size: {batchSizes.Average():N0}");
            }
            else
            {
                Console.WriteLine("Average batch size: 0 (No batches were sent)");
            }
        }
    }
}