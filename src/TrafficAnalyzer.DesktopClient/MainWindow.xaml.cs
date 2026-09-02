using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using TrafficAnalyzer.Core;
using System.Threading.Channels;
using System.Threading;
using Windows.UI;
using TrafficAnalyzer.Capture;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TrafficAnalyzer.DesktopClient
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        //dotnet run -r win-x64
        private const int SQUARE_SIZE = 3;
        private const int GAP = 1;
        private const int CELL_SIZE = SQUARE_SIZE + GAP;

        private readonly Channel<ParsedPacket> _channel = Channel.CreateUnbounded<ParsedPacket>();
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly object _gridLock = new();
        private Color[] _gridColors = Array.Empty<Color>();
        private int _currentIndex = 0;
        private int _cols, _rows, _totalSquares;

        private readonly object _statsLock = new();
        private readonly int[] _lagSums = new int[10];
        private readonly int[] _lagCounts = new int[10];
        private readonly int[] _batchCounts = new int[10];
        private readonly int[] _packetCounts = new int[10];
        private int _currentBucket = 0;

        public MainWindow()
        {
            InitializeComponent();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        public void StartVisualisation(ICapture capture)
        {
            Task.Run(() => ConsumePacketsAsync(_channel.Reader));
            capture.StartCapture(_channel.Writer, _cts);
        }

        private async Task ConsumePacketsAsync(ChannelReader<ParsedPacket> reader)
        {
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out ParsedPacket packet))
                {
                    var lag = (int)(DateTime.UtcNow - packet.captureTime).TotalMilliseconds;

                    lock (_statsLock)
                    {
                        _lagSums[_currentBucket] += Math.Max(0, lag);
                        _lagCounts[_currentBucket]++;
                        _packetCounts[_currentBucket]++;
                    }

                    Color c = GetPacketColor(packet);

                    lock (_gridLock)
                    {
                        if (_totalSquares == 0)
                        {
                            continue;
                        }

                        _gridColors[_currentIndex] = c;
                        _currentIndex++;

                        if (_currentIndex >= _totalSquares)
                        {
                            Array.Clear(_gridColors, 0, _gridColors.Length);
                            _currentIndex = 0;
                        }
                    }
                }


                lock (_statsLock)
                {
                    _batchCounts[_currentBucket]++;
                }

                DispatcherQueue.TryEnqueue(() => GridCanvas.Invalidate());
            }
        }

        private void GridCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            args.DrawingSession.Clear(Color.FromArgb(255, 31, 36, 40)); // #1f2428 background

            lock (_gridLock)
            {
                for (int i = 0; i < _currentIndex; i++)
                {
                    int col = i % _cols;
                    int row = i / _cols;
                    float x = col * CELL_SIZE;
                    float y = row * CELL_SIZE;

                    args.DrawingSession.FillRectangle(x, y, SQUARE_SIZE, SQUARE_SIZE, _gridColors[i]);
                }
            }
        }

        private void GridCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            lock (_gridLock)
            {
                _cols = (int)(e.NewSize.Width / CELL_SIZE);
                _rows = (int)(e.NewSize.Height / CELL_SIZE);
                _totalSquares = _cols * _rows;

                if (_totalSquares > 0)
                {
                    _gridColors = new Color[_totalSquares];
                    _currentIndex = 0;
                }
            }
        }

        private void Timer_Tick(object sender, object e)
        {
            int totalLag = 0, totalLagCount = 0, totalBatches = 0, totalPackets = 0;

            lock (_statsLock)
            {
                for (int i = 0; i < 10; i++)
                {
                    totalLag += _lagSums[i];
                    totalLagCount += _lagCounts[i];
                    totalBatches += _batchCounts[i];
                    totalPackets += _packetCounts[i];
                }

                // Shift bucket
                _currentBucket = (_currentBucket + 1) % 10;
                _lagSums[_currentBucket] = 0;
                _lagCounts[_currentBucket] = 0;
                _batchCounts[_currentBucket] = 0;
                _packetCounts[_currentBucket] = 0;
            }

            double avgLag = totalLagCount == 0 ? 0 : (double)totalLag / totalLagCount;

            PpsValue.Text = totalPackets.ToString("N0");
            BpsValue.Text = totalBatches.ToString("N0");
            LagValue.Text = avgLag.ToString("F1");

            // Update Lag Color
            if (avgLag < 50) LagValue.Foreground = new SolidColorBrush(Color.FromArgb(255, 63, 185, 80));
            else if (avgLag < 500) LagValue.Foreground = new SolidColorBrush(Color.FromArgb(255, 227, 179, 65));
            else LagValue.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 123, 114));
        }

        private Color GetPacketColor(ParsedPacket p)
        {
            if (p.etherType != 2048) return Color.FromArgb(255, 255, 123, 114); // #ff7b72
            return p.networkProtocol switch
            {
                6 => Color.FromArgb(255, 88, 166, 255),   // #58a6ff
                17 => Color.FromArgb(255, 63, 185, 80),   // #3fb950
                1 => Color.FromArgb(255, 227, 179, 65),   // #e3b341
                _ => Color.FromArgb(255, 188, 140, 255)   // #bc8cff
            };
        }
    }
}
