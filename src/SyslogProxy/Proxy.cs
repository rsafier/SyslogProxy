namespace SyslogProxy
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using SyslogProxy.Messages;

    public enum ProxyMode
    {
        TCP,
        UDP
    }
    public class Proxy
    {
        private readonly Action<string> messageHandler;

        private readonly CancellationToken cancellationToken;

        private const int BufferSize = 2048;

        public Proxy(Action<string> messageHandler, CancellationToken cancellationToken, ProxyMode proxyMode = ProxyMode.TCP)
        {
            this.messageHandler = messageHandler;
            this.cancellationToken = cancellationToken;
            if (proxyMode == ProxyMode.TCP)
            {
                var tcp = new TcpListener(IPAddress.Any, Configuration.ProxyPort);

                tcp.Start();
                this.AcceptTCPConnection(tcp).ConfigureAwait(false);
            }
            else
            {
                AcceptUDPConnection().ConfigureAwait(false);

            }
        }

        private async Task AcceptTCPConnection(TcpListener listener)
        {
            while (!this.cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                this.EchoAsync(client).ConfigureAwait(false);
            }
        }

        private async Task AcceptUDPConnection()
        {
            UdpClient aa = new UdpClient(Configuration.ProxyPort);

            try
            {
                IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, Configuration.ProxyPort);

                while (!this.cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("Waiting for broadcast");
                    byte[] bytes = aa.Receive(ref groupEP);

                    Console.WriteLine($"Received broadcast from {groupEP} :");
                    Console.WriteLine($" {Encoding.ASCII.GetString(bytes, 0, bytes.Length)}");
                    EchoAsync(bytes).ConfigureAwait(false);
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                aa.Close();
            }

        }
        private async Task EchoAsync(byte[] data)
        {

            var accumulator = new StringBuilder();
              
                accumulator.Append(Encoding.UTF8.GetString(data).TrimEnd('\0'));
                if (accumulator.ToString().Contains("\n"))
                {
                    var splitMessage = accumulator.ToString().Split('\n').ToList();
                    accumulator = new StringBuilder(splitMessage.Last());
                    splitMessage.RemoveAt(splitMessage.Count - 1);
                    splitMessage.ForEach(this.messageHandler);
                }
        }
        private async Task EchoAsync(TcpClient client)
        {
            var ipAddress = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            Logger.Information("New client connected from IP: [{0}].", ipAddress);
            using (client)
            {
                var stream = client.GetStream();
                var buf = new byte[BufferSize];
                var accumulator = new StringBuilder();
                while (true)
                {
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Configuration.TcpConnectionTimeout));
                    Array.Clear(buf, 0, BufferSize);
                    var amountReadTask = stream.ReadAsync(buf, 0, buf.Length, this.cancellationToken);
                    var completedTask = await Task.WhenAny(timeoutTask, amountReadTask)
                                                  .ConfigureAwait(false);
                    if (completedTask == timeoutTask)
                    {
                        Logger.Information("Client with IP [{0}] timed out.", ipAddress);
                        break;
                    }

                    if (amountReadTask.Result == 0)
                    {
                        break;
                    }
                    accumulator.Append(Encoding.UTF8.GetString(buf).TrimEnd('\0'));
                    if (accumulator.ToString().Contains("\n"))
                    {
                        var splitMessage = accumulator.ToString().Split('\n').ToList();
                        accumulator = new StringBuilder(splitMessage.Last());
                        splitMessage.RemoveAt(splitMessage.Count - 1);
                        splitMessage.ForEach(this.messageHandler);
                    }
                }
            }
            Logger.Information("Client with IP [{0}] disconnected.", ipAddress);
        }
    }
}