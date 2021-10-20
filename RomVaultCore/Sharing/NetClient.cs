using System;
using System.Net;
using System.Net.Sockets;
using System.Timers;

namespace Server
{
    public class NetClient
    {
        private int _port;
        private IPAddress _ip;

        public event ReceivedData OnReceivedData;

        public delegate byte[] ReceivedData(string ip,byte[] buffer);

        private TcpListener _serverSocket;

        private Timer _timer;

        public bool StartListener(IPAddress ipAddress, int port, out string error)
        {
            _ip = ipAddress;
            _port= port;
            error = string.Empty;

            if (!StopStart(out error))
                return false;

            _timer = new Timer(30*60*1000);
            _timer.Elapsed += OnTimedEvent;
            _timer.AutoReset = true;
            _timer.Enabled = true;

            return true;
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            StopStart(out string error);
        }

        private bool StopStart(out string error)
        {
            _serverSocket?.Stop();
            try
            {
                _serverSocket = new TcpListener(_ip, _port);
                _serverSocket.Start();
            }
            catch (Exception e)
            {
                error = e.ToString();
                return false;
            }
            Listen();
            error = "";
            return true;

        }

        public void StopListener()
        {
            _timer.Enabled = false;
            if (_serverSocket == null)
            {
                return;
            }

            _serverSocket.Stop();
            _serverSocket = null;
        }




        public async void Listen()
        {
            while (true)
            {
                try
                {
                    TcpClient client = await _serverSocket.AcceptTcpClientAsync();

                    _timer.Stop();
                    _timer.Start();

                    string ip=client.Client.RemoteEndPoint.AddressFamily.ToString();

                    IPEndPoint remoteIpEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                    IPEndPoint localIpEndPoint = client.Client.LocalEndPoint as IPEndPoint;


                    NetworkStream stream = client.GetStream();

                    byte[] bufferSend = StreamHelper.GetBytes(stream);

                    byte[] bufferReply = OnReceivedData?.Invoke(remoteIpEndPoint?.Address.ToString(),bufferSend);

                    if (bufferReply != null)
                    {
                        StreamHelper.SendBytes(stream, bufferReply);
                    }
                    stream.Close();
                }
                catch (SocketException socketException)
                {
                    Console.WriteLine(socketException);
                    return;
                }
                catch (InvalidOperationException invalidOperationException)
                {
                    Console.WriteLine(invalidOperationException);
                    return;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return;
                }
            }
        }


        public static bool SendToServer(byte[] payload, string ipAddress, int port, out byte[] response)
        {
            // Default output
            response = null;

            // Check input
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                Console.WriteLine("SendToClient: ipAddress is null");
                return false;
            }

            if (!IPAddress.TryParse(ipAddress, out IPAddress address))
            {
                Console.WriteLine("SendToClient: ipAddress is invalid");
                return false;
            }

            using (var client = new TcpClient())
            {

                client.ReceiveTimeout = 60000;
                client.SendTimeout = 60000;

                try
                {
                    Console.WriteLine($"SendToClient: connecting to {address} : {port}");
                    //client.Connect(address, port);

                    if (!client.ConnectAsync(address, port).Wait(5000))
                    {
                        Console.WriteLine("SendToClient: connection timeout.");
                        return false;
                    }

                    //Console.WriteLine("SendtoClient getting stream");
                    using (NetworkStream stream = client.GetStream())
                    {
                        response = StreamHelper.SendGetBytes(stream, payload);
                    }

                }
                catch (Exception e)
                {
                    Console.WriteLine($"SendToClient Error: e.Message = {e.Message}");
                    if (e.InnerException != null)
                        Console.WriteLine($"SendToClient Error: e.InnerException.Message = {e.InnerException.Message}");
                    return false;
                }


            }

            return true;
        }


    }
}