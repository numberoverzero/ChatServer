using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Engine.DataStructures;
using Engine.Networking;
using Engine.Networking.Packets;
using Engine.Utility;

namespace ChatProgram
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            PacketGlobals.Initialize();

            if (args.Length < 1) return;
            var mode = args[0];

            switch (mode.ToLower())
            {
                case "client":
                    ChatClientRunner.Run();
                    break;
                case "server":
                    {
                        var port = int.Parse(args[1]);
                        var logfile = args.Length > 2 ? args[2] : null;

                        ChatServerRunner.Run(port, logfile);
                    }
                    break;
            }
        }
    }

    public static class ChatClientRunner
    {
        public static void Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var client = new ChatClient();
            Application.Run(client.Form);
        }
    }

    public static class ChatServerRunner
    {
        public static void Run(int port, string logfile)
        {
            new ChatServer(IPAddress.Any, port, logfile).Start();
        }
    }

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
                    tcpClient.Connect(host, port);
                    Client = new Client(tcpClient);
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
            var client = sender as Client;
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

    public class ChatServer : BasicServer
    {
        private readonly BidirectionalDict<Client, string> nickTable;

        public ChatServer(IPAddress localaddr, int port, string logfile)
            : base(localaddr, port, logfile)
        {
            nickTable = new BidirectionalDict<Client, string>();
            OnDisconnect += Handle_OnDisconnect;
            OnConnect += DefaultHandle_OnConnect;
        }

        protected void Handle_OnDisconnect(object sender, ServerEventArgs args)
        {
            var client = args.Client;
            var ip = client.GetIP;
            string nick = null;
            try
            {
                nick = nickTable[client];
                nickTable.Remove(client);
                Log.Info("Server:Disconnect:DisassociatedNick:Data:Nick:<{0}>".format(nick));
                SendPacket(MakePacket("{0} has left the chat.".Timestamped().format(nick)));
            }
            catch (KeyNotFoundException)
            {
                Log.Warn("Exception:ServerException:UnknownNickAssociation:Data:Key:<{0}>".format(ip));
                Log.Warn("Exception:ServerException:UnknownNickAssociation:Data:PossibleValue:<{0}>".format(nick));
                Log.Warn(
                    "Exception:ServerException:UnknownNickAssociation:Data:Note:User may not have authenticated with the server before disconnecting."
                        .format(nick));
            }
        }

        private bool IsNickAvailable(string nick)
        {
            return !(String.IsNullOrEmpty(nick) || nickTable.Contains(nick));
        }

        public override void ReceivePacket(Packet packet, Client client)
        {
            if (!IsAuthenticated(client))
            {
                var authPacket = packet as AuthRequestPacket;
                if (authPacket == null) return;

                HandleAutheticatePacket(authPacket, client);
                return;
            }

            var chatPacket = packet as ChatPacket;
            var nick = nickTable[client];

            if (chatPacket == null) return;
            if (String.IsNullOrEmpty(chatPacket.Message)) return;

            SendPacket(MakePacket("{0}: {1}".format(nick, chatPacket.Message)));
        }

        private void HandleAutheticatePacket(AuthRequestPacket requestPacket, Client client)
        {
            const string nickInUseKey = "Server:AuthFail:Reason:NickInUse:Data:Nick:<{0}>";
            var username = requestPacket.UserName;
            var args = new ServerEventArgs(false, client);
            if (!IsNickAvailable(username))
            {
                Log.Info(nickInUseKey.format(username));
                SendPacket(new AuthResponsePacket() { Success = false, Message = "NickInUse:{0}".format(username) }, client);
            }
            else
            {
                nickTable[client] = username;
                SendPacket(new AuthResponsePacket() { Success = true, Message = username }, client);
                SendServerMessage("Please welcome {0} to the server!".format(username));
                args.Success = true;
            }
            Authenticate(client, args);
        }

        private void SendServerMessage(string msg, params Client[] clients)
        {
            SendPacket(MakePacket("Server: {0}".format(msg)), clients);
        }

        private static ChatPacket MakePacket(string msg)
        {
            return new ChatPacket { Message = msg };
        }

        protected override void DefaultHandle_OnConnect(object sender, ServerEventArgs args)
        {
            base.DefaultHandle_OnConnect(sender, args);
            var client = args.Client;
            SendServerMessage("Welcome to the server!  Please select a username:", client);
        }
    }

    public class ChatPacket : Packet
    {
        public string Message;

        public override Packet Copy()
        {
            return new ChatPacket();
        }

        public override void BuildAsByteArray(ByteArrayBuilder builder)
        {
            base.BuildAsByteArray(builder);
            builder.Add(Message);
        }

        protected override int ReadFromByteArray(ByteArrayReader reader)
        {
            base.ReadFromByteArray(reader);
            Message = reader.ReadString();
            return reader.Index;
        }
    }

    public class AuthRequestPacket : Packet
    {
        public string UserName;

        public override Packet Copy()
        {
            return new AuthRequestPacket();
        }

        public override void BuildAsByteArray(ByteArrayBuilder builder)
        {
            base.BuildAsByteArray(builder);
            builder.Add(UserName);
        }

        protected override int ReadFromByteArray(ByteArrayReader reader)
        {
            base.ReadFromByteArray(reader);
            UserName = reader.ReadString();
            return reader.Index;
        }
    }

    public class AuthResponsePacket : Packet
    {
        public bool Success;
        public string Message;

        public override Packet Copy()
        {
            return new AuthResponsePacket();
        }

        public override void BuildAsByteArray(ByteArrayBuilder builder)
        {
            base.BuildAsByteArray(builder);
            builder.Add(Success);
            builder.Add(Message);
        }

        protected override int ReadFromByteArray(ByteArrayReader reader)
        {
            base.ReadFromByteArray(reader);
            Success = reader.ReadBool();
            Message = reader.ReadString();
            return reader.Index;
        }
    }

    public static class PacketGlobals
    {
        public static void Initialize()
        {
            var builder = new PacketBuilder();
            builder.RegisterPackets(
                new ChatPacket(),
                new AuthRequestPacket(),
                new AuthResponsePacket());
            Packet.Builder = builder;
        }
    }
}