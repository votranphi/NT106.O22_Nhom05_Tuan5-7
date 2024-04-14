using System.Drawing;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Forms;

namespace Client
{
    public partial class ClientForm : Form
    {
        private TcpClient tcpClient;
        private Thread clientThread;
        private bool isClientRunning = false;
        private SslStream sslStream;
        // maximum buffer size can read to
        private static int buff_size = 2048;
        private byte[] buffer = new byte[buff_size];
        private delegate void SafeCallDelegate(string text);
        private delegate void SafeCallDelegateImage(Bitmap bmp);

        public ClientForm()
        {
            InitializeComponent();
        }

        // The following method is invoked by the RemoteCertificateValidationDelegate.
        public static bool ValidateServerCertificate(
              object sender,
              X509Certificate certificate,
              X509Chain chain,
              SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            // MessageBox.Show($"Certificate error: {sslPolicyErrors}");

            // Do not allow this client to communicate with unauthenticated servers.
            return true;
        }

        private void receiveFromServer()
        {
            try
            {
                while (isClientRunning)
                {
                    int readBytes = sslStream.Read(buffer);
                    string msg = Encoding.UTF8.GetString(buffer, 0, readBytes);

                    if (msg == "<Invalid_Username_Exists>")
                    {
                        UpdateChatHistoryThreadSafe($"Username already exists, please pick another one!\n");
                        isClientRunning = false;
                        UpdateConnectButtonTextThreadSafe("Connect");
                        break;
                    }
                    if (msg == "<Accepted>")
                    {
                        UpdateChatHistoryThreadSafe($"[{DateTime.Now}] Connected to the server with ip address {ipInput.Text} on port {portInput.Text}\n");
                        continue;
                    }

                    if (msg == "<Image>")
                    {
                        // maximum size of image is 524288 bytes
                        byte[] bytes = new byte[524288];
                        // wait for server side to complete writing data
                        Thread.Sleep(1000);
                        sslStream.Read(bytes);

                        Bitmap bmp;
                        using (var ms = new MemoryStream(bytes))
                        {
                            bmp = new Bitmap(ms);
                        }


                        UpdateImageThreadSafe(bmp);

                        continue;
                    }

                    // the if... below resolves the Problem 1 in ServerForm.cs
                    if (msg != null && msg != "")
                    {
                        UpdateChatHistoryThreadSafe($"{msg}\n");
                    }
                }
            }
            catch (SocketException sockEx)
            {
                tcpClient.Close();
                sslStream.Close();
            }
        }

        private void connectBtn_Click(object sender, EventArgs e)
        {
            try
            {
                if (isClientRunning)
                {
                    isClientRunning = false;
                    // streamWriter.WriteLine("<Disconnect>");
                    byte[] bufferWr = Encoding.UTF8.GetBytes("<Disconnect>");
                    sslStream.Write(bufferWr);
                    sslStream.Flush();
                    tcpClient = null;
                    statusAndMsg.Text += $"[{DateTime.Now}] Disconnected from the server with ip address {ipInput.Text} on port {portInput.Text}\n";
                    connectBtn.Text = "Connect";
                }
                else
                {
                    isClientRunning = true;
                    tcpClient = new TcpClient();
                    tcpClient.Connect(new IPEndPoint(IPAddress.Parse(ipInput.Text), int.Parse(portInput.Text)));

                    // Create an SSL stream that will close the client's stream.
                    sslStream = new SslStream(
                        tcpClient.GetStream(),
                        false,
                        new RemoteCertificateValidationCallback(ValidateServerCertificate),
                        null
                        );
                    // The server name must match the name on the server certificate.
                    try
                    {
                        sslStream.AuthenticateAsClient("MySslSocketCertificate");
                    }
                    catch (AuthenticationException ex)
                    {
                        MessageBox.Show($"Exception: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            MessageBox.Show($"Inner exception: {ex.InnerException.Message}");
                        }
                        MessageBox.Show("Authentication failed - closing the connection.");
                        tcpClient.Close();
                        return;
                    }

                    // send the username to the server first
                    byte[] bufferWr = Encoding.UTF8.GetBytes(usernameInput.Text);
                    sslStream.Write(bufferWr);
                    sslStream.Flush();

                    clientThread = new Thread(this.receiveFromServer);
                    clientThread.Start();
                    connectBtn.Text = "Disconnect";
                }
            }
            catch (SocketException sockEx)
            {
                MessageBox.Show(sockEx.Message, "Network error", MessageBoxButtons.OKCancel, MessageBoxIcon.Error);
                isClientRunning = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void sendBtn_Click(object sender, EventArgs e)
        {
            try
            {
                if (tcpClient != null && tcpClient.Connected)
                {
                    byte[] bufferWr = Encoding.UTF8.GetBytes(msgToSend.Text);
                    sslStream.Write(bufferWr);
                    sslStream.Flush();
                    statusAndMsg.Text += $"[{DateTime.Now}] {usernameInput.Text}: {msgToSend.Text}\n";
                    msgToSend.Text = "";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void msgToSend_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == 13)
            {
                if (sendBtn.Enabled)
                {
                    sendBtn_Click(sender, e);
                }
            }
        }

        private void emoBtn_Click(object sender, EventArgs e)
        {
            msgToSend.Focus();
        }

        private void imageBtn_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Image file|*.jpg";

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                // convert a image into byte array
                Image image = Image.FromFile(ofd.FileName);
                var ms = new MemoryStream();
                image.Save(ms, image.RawFormat);
                byte[] bytes = ms.ToArray();

                // send the message and byte array
                byte[] bufferWr = Encoding.UTF8.GetBytes("<Image>");
                sslStream.Write(bufferWr);
                sslStream.Flush();
                sslStream.Write(bytes);
                sslStream.Flush();
            }
        }

        private void emojiCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            msgToSend.Text += emojiCB.Text;
            emojiCB.SelectedIndex = -1;
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

        private void UpdateConnectButtonTextThreadSafe(string text)
        {
            if (connectBtn.InvokeRequired)
            {
                var d = new SafeCallDelegate(UpdateConnectButtonTextThreadSafe);
                connectBtn.Invoke(d, new object[] { text });
            }
            else
            {
                connectBtn.Text = text;
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
                statusAndMsg.Text += "\n";
                // move cursor to the end of the RTB
                statusAndMsg.Select(statusAndMsg.Text.Length - 1, 0);
                // scroll to cursor the RTB
                statusAndMsg.ScrollToCaret();

                Clipboard.SetDataObject(bmp);
                statusAndMsg.Paste();
            }
        }

        #endregion

        #region Responsive

        private void ipInput_TextChanged(object sender, EventArgs e)
        {
            if (ipInput.Text != "" && portInput.Text != "" && usernameInput.Text != "")
            {
                connectBtn.Enabled = true;
            }
            else
            {
                connectBtn.Enabled = false;
            }
        }

        private void portInput_TextChanged(object sender, EventArgs e)
        {
            if (ipInput.Text != "" && portInput.Text != "" && usernameInput.Text != "")
            {
                connectBtn.Enabled = true;
            }
            else
            {
                connectBtn.Enabled = false;
            }
        }

        private void usernameInput_TextChanged(object sender, EventArgs e)
        {
            if (ipInput.Text != "" && portInput.Text != "" && usernameInput.Text != "")
            {
                connectBtn.Enabled = true;
            }
            else
            {
                connectBtn.Enabled = false;
            }
        }

        private void connectBtn_TextChanged(object sender, EventArgs e)
        {
            if (connectBtn.Text == "Connect")
            {
                ipInput.Enabled = true;
                portInput.Enabled = true;
                usernameInput.Enabled = true;
                imageBtn.Enabled = false;
            }
            else
            {
                ipInput.Enabled = false;
                portInput.Enabled = false;
                usernameInput.Enabled = false;
                imageBtn.Enabled = true;
            }
        }

        private void msgToSend_TextChanged(object sender, EventArgs e)
        {
            if (msgToSend.Text == "")
            {
                sendBtn.Enabled = false;
            }
            else
            {
                if (connectBtn.Text == "Connect")
                {
                    sendBtn.Enabled = false;
                }
                else
                {
                    sendBtn.Enabled = true;
                }
            }
        }

        #endregion
    }
}