using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Engine.Networking;
using Engine.Utility;

namespace ChatProgram
{
    public class ChatClient
    {
        public enum State
        {
            PreConnection,
            ConnectedWithoutAuthentication,
            AwaitingAuthenticationResponse,
            ConnectedWithAuthentication,
            PostConnection
        }

        private const string EnterIP = "Please enter the server's IP address and port.";
        private const string ValidIP = "Now connected to {0}:{1}!";
        private const string FailedIP = "Failed to connect to {0}:{1}.";
        private const string InvalidIP = "Invalid format.  Format is (for example) 192.168.1.1:5000";
        private const string TryingIP = "Trying to connect...";
        public readonly TextBox ChatEnterTextBox;
        public readonly TextBox ChatHistoryTextBox;
        public Client Client;
        public State ConnectionState;
        public Form Form;

        private string host;
        private int port;
        private string nick;

        public ChatClient()
        {
            Form = new Form();
            Title = "OFFLINE";

            ChatHistoryTextBox = new TextBox {Dock = DockStyle.Fill, Multiline = true, ReadOnly = true};
            ChatEnterTextBox = new TextBox {Dock = DockStyle.Bottom};

            Form.Controls.Add(ChatEnterTextBox);
            Form.Controls.Add(ChatHistoryTextBox);

            Form.Show();
            ChatEnterTextBox.Focus();
            ChatEnterTextBox.KeyDown += OnKeyDown;

            ConnectionState = State.PreConnection;

            Form.Closing += HandleClientClose;
            WriteToHistory(EnterIP);
        }

        private string Title
        {
            get { return Form.Text; }
            set { Form.Text = value; }
        }

        [DllImport("kernel32.dll")]
        private static extern void ExitProcess(int a);

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            var msg = ChatEnterTextBox.Text;
            switch (ConnectionState)
            {
                case State.PreConnection:
                    TryConnect(msg);
                    break;
                case State.ConnectedWithoutAuthentication:
                    Client.WritePacket(new AuthRequestPacket {UserName = msg});
                    ConnectionState = State.AwaitingAuthenticationResponse;
                    break;
                case State.AwaitingAuthenticationResponse:
                    // Don't send the message or clear the text box - we haven't gotten a response from the server regarding user name.
                    return;
                case State.ConnectedWithAuthentication:
                    Client.WritePacket(new ChatPacket {Message = msg});
                    break;
                case State.PostConnection:
                    const string err = "No connection to server.  The following message was not sent: <{0}>";
                    WriteToHistory(err.format(msg));
                    break;
            }
            ChatEnterTextBox.Text = "";
        }

        private void TryConnect(string msg)
        {
            var tcpClient = new TcpClient();
            WriteToHistory(TryingIP);
            var parts = msg.Split(':');
            if (parts.Length == 2)
            {
                host = parts[0];
                port = int.Parse(parts[1]);
                try
                {
                    var baseClient = new TcpClient(host, port);
                    Client = new Client(baseClient);
                    Client.OnReadPacket += OnClientRead;
                    WriteToHistory(ValidIP.format(host, port));
                    ConnectionState = State.ConnectedWithoutAuthentication;
                    Title = "CONNECTED: <{0}:{1}>".format(host, port);
                    return;
                }
                catch
                {
                    WriteToHistory(FailedIP.format(host, port));
                }
            }
            else
            {
                WriteToHistory(InvalidIP);
            }
            // Failed connection, so prompt to try again
            WriteToHistory(EnterIP);
        }

        private void WriteToHistory(string msg, bool newline = true)
        {
            if (msg == null) return;
            if (newline) msg = msg + "\r\n";
            ChatHistoryTextBox.InvokeEx(c => c.AppendText(msg));
        }

        /// <summary>
        ///   Called when the client receives a new packet
        /// </summary>
        /// <param name="sender"> </param>
        /// <param name="args"> </param>
        protected void OnClientRead(object sender, PacketArgs args)
        {
            var client = args.Client;
            if (client == null) return;

            var packet = args.Packet;
            if (packet == null) return;

            var authResponsePacket = packet as AuthResponsePacket;
            if (authResponsePacket != null && ConnectionState == State.AwaitingAuthenticationResponse)
            {
                var authMsg = authResponsePacket.Message;
                if (!authResponsePacket.Success)
                {
                    if (authMsg.StartsWith("NickInUse:"))
                    {
                        var badNick = authMsg.Split(new char[] {':'}, 2)[1];
                        WriteToHistory("Sorry, the nickname '{0}' is already taken.".format(badNick));
                    }
                    WriteToHistory("Please select a different username:");
                    return;
                }
                "CONNECTED as <{0}>: <{1}:{2}>".format(authMsg, host, port);
                ConnectionState = State.ConnectedWithAuthentication;
                return;
            }

            var chatPacket = packet as ChatPacket;
            if (chatPacket == null) return;
            WriteToHistory(chatPacket.Message);
        }

        private void HandleClientClose(object sender, CancelEventArgs e)
        {
            try
            {
                Client.Close();
            }
            catch
            {
            }
            e.Cancel = false;
            Application.Exit();
            ExitProcess(0);
        }
    }
}