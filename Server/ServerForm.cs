using System.Net.Sockets;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;

namespace Server
{
    public partial class ServerForm : Form
    {
        private TcpListener tcpListener;
        private Thread listenThread;
        private bool isServerRunning = false;
        private Dictionary<string, TcpClient> listOfClients = new Dictionary<string, TcpClient>();
        private delegate void SafeCallDelegate(string text);
        private delegate void SafeCallDelegateImage(Bitmap bmp);

        private X509Certificate serverCertificate = null;
        // maximum buffer size can read to
        private static int buff_size = 2048;
        private byte[] buffer = new byte[buff_size];

        public ServerForm()
        {
            InitializeComponent();
        }

        private void listen()
        {
            try
            {
                serverCertificate = new X509Certificate("Socket.pfx", "136900");
                tcpListener = new TcpListener(new IPEndPoint(IPAddress.Parse(ipInput.Text), int.Parse(portInput.Text)));
                tcpListener.Start();

                while (isServerRunning)
                {
                    // accepts if there is a client
                    TcpClient _client = tcpListener.AcceptTcpClient();

                    SslStream sslStream = new SslStream(_client.GetStream(), false);
                    sslStream.AuthenticateAsServer(serverCertificate, false, true);

                    // receive the username from connected client
                    int readBytes = sslStream.Read(buffer);
                    string username = Encoding.UTF8.GetString(buffer, 0, readBytes);
                    if (listOfClients.ContainsKey(username))
                    {
                        byte[] bufferWr = Encoding.UTF8.GetBytes("<Invalid_Username_Exists>");
                        sslStream.Write(bufferWr);
                        // _client.Close();
                    }
                    else
                    {
                        byte[] bufferWr = Encoding.UTF8.GetBytes("<Accepted>");
                        sslStream.Write(bufferWr);
                        sslStream.Flush();
                        Thread _clientThread = new Thread(() => this.receiveFromClient(username, _client, sslStream));
                        listOfClients.Add(username, _client);
                        _clientThread.Start();

                        string clientIPAddress = ((IPEndPoint)_client.Client.RemoteEndPoint).Address.ToString();
                        string clientPort = ((IPEndPoint)_client.Client.RemoteEndPoint).Port.ToString();
                        UpdateChatHistoryThreadSafe($"[{DateTime.Now}] Accept connection from {username} ({clientIPAddress}:{clientPort})\n");
                    }
                }
            }
            catch (SocketException ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void receiveFromClient(string username, TcpClient _client, SslStream sslStream)
        {
            try
            {
                while (isServerRunning)
                {
                    int readBytes = sslStream.Read(buffer);
                    string msgFromClient = Encoding.UTF8.GetString(buffer, 0, readBytes);
                    if (msgFromClient == "<Disconnect>")
                    {
                        listOfClients.Remove(username);
                        // Problem 1: two lines below will send the empty string to the client after close
                        _client.Close();
                        sslStream.Close();
                        UpdateChatHistoryThreadSafe($"[{DateTime.Now}] {username} disconnected from server!\n");
                        break;
                    }

                    if (msgFromClient == "<Image>")
                    {
                        // maximum size of image is 33000 bytes
                        byte[] bytes = new byte[33000];
                        // wait for client side to complete writing data
                        Thread.Sleep(1000);
                        sslStream.Read(bytes);

                        foreach (TcpClient i in listOfClients.Values)
                        {
                            if (i != _client)
                            {
                                SslStream _sslStream = new SslStream(i.GetStream());
                                byte[] bufferWr = Encoding.UTF8.GetBytes("<Image>");
                                _sslStream.Write(bufferWr);
                                _sslStream.Flush();
                                _sslStream.Write(bytes);
                                _sslStream.Flush();
                            }
                        }

                        // image view
                        Bitmap bmp;
                        using (var ms = new MemoryStream(bytes))
                        {
                            bmp = new Bitmap(ms);
                        }
                        UpdateImageThreadSafe(bmp);

                        continue;
                    }

                    string formattedMsg = $"[{DateTime.Now}] {username}: {msgFromClient}";

                    // send the received message to all the clients except the incoming one
                    foreach (TcpClient i in listOfClients.Values)
                    {
                        if (i != _client)
                        {
                            SslStream _sslStream = new SslStream(i.GetStream());
                            byte[] bufferWr = Encoding.UTF8.GetBytes(formattedMsg);
                            _sslStream.Write(bufferWr);
                            _sslStream.Flush();
                        }
                    }

                    // message view
                    UpdateChatHistoryThreadSafe(formattedMsg + "\n");
                }
            }
            catch (SocketException sockEx)
            {
                _client.Close();
                sslStream.Close();
            }
        }

        private void listenBtn_Click(object sender, EventArgs e)
        {
            if (isServerRunning)
            {
                isServerRunning = false;
                tcpListener.Stop();
                listenThread = null;
                statusAndMsg.Text += $"[{DateTime.Now.ToString()}] Stop listening with ip address {ipInput.Text} on port {portInput.Text}\n";
                listenBtn.Text = "Listen";
            }
            else
            {
                isServerRunning = true;
                listenThread = new Thread(this.listen);
                listenThread.Start();
                statusAndMsg.Text += $"[{DateTime.Now.ToString()}] Start listening with ip address {ipInput.Text} on port {portInput.Text}\n";
                listenBtn.Text = "Stop";
            }
        }

        #region UpdateThreadSafe
        private void UpdateChatHistoryThreadSafe(string text)
        {
            if (statusAndMsg.InvokeRequired)
            {
                var d = new SafeCallDelegate(UpdateChatHistoryThreadSafe);
                statusAndMsg.Invoke(d, new object[] { text });
            }
            else
            {
                statusAndMsg.Text += text;
            }
        }

        private void UpdateImageThreadSafe(Bitmap bmp)
        {
            if (statusAndMsg.InvokeRequired)
            {
                var d = new SafeCallDelegateImage(UpdateImageThreadSafe);
                statusAndMsg.Invoke(d, new object[] { bmp });
            }
            else
            {
                Clipboard.SetDataObject(bmp);
                statusAndMsg.Text += "\n";
                // move cursor to the end of the RTB
                statusAndMsg.Select(statusAndMsg.Text.Length - 1, 0);
                // scroll to the end of the RTB
                statusAndMsg.ScrollToCaret();
                statusAndMsg.Paste();
            }
        }
        #endregion

        #region Responsive



        #endregion
    }
}