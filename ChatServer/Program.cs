using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;

using Engine.DataStructures;
using Engine.Networking;
using Engine.Networking.Packets;

using Engine.Utility;

namespace ChatServer
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1) return;
            var portString = args[0];
            string logfile = null;
            if (args.Length > 1)
                logfile = args[1];
            int port = int.Parse(portString);
            var server = new ChatServer(IPAddress.Any, port, logfile);
            server.Start();
        }
    }

    public class ChatServer : BasicServer
    {
        BidirectionalDict<Client, string> nickTable;

        public ChatServer(IPAddress localaddr, int port, string logfile) : base(localaddr, port, logfile)
        {
            nickTable = new BidirectionalDict<Client, string>();
            OnDisconnect += Handle_OnDisconnect;
            OnConnect += DefaultHandle_OnConnect;
            OnConnect += Authenticate_OnConnect;
        }

        protected void Authenticate_OnConnect(object sender, ServerEventArgs args)
        {
            var client = args.Client;
            try
            {
                Authenticate(client);
                var nick = nickTable[client];
                log.Info("Server:Connect:AssociatedNick:Data:Nick:<{0}>".format(nick));
                SendPacket(MakePacket("{0} has joined the chat.".Timestamped().format(nick)));
            }
            catch { }
        }

        protected void Handle_OnDisconnect(object sender, ServerEventArgs args)
        {
            var client = args.Client;
            string ip = client.IPString;
            string nick = null;
            try
            {
                nick = nickTable[client];
                nickTable.Remove(client);
                log.Info("Server:Disconnect:DisassociatedNick:Data:Nick:<{0}>".format(nick));
                SendPacket(MakePacket("{0} has left the chat.".Timestamped().format(nick)));
            }
            catch (KeyNotFoundException)
            {
                log.Warn("Exception:ServerException:UnknownNickAssociation:Data:Key:<{0}>".format(ip));
                log.Warn("Exception:ServerException:UnknownNickAssociation:Data:PossibleValue:<{0}>".format(nick));
                log.Warn("Exception:ServerException:UnknownNickAssociation:Data:Note:User may not have authenticated with the server before disconnecting.".format(nick));
            }
        }

        bool IsNickAvailable(string nick) { return !(String.IsNullOrEmpty(nick) || nickTable.HasItem(nick)); }

        public override void Authenticate(Client client, ServerEventArgs e = null)
        {
            string nick = null;
            const string nickInUseKey = "Server:AuthFail:Reason:NickInUse:Data:Nick";
            if (e == null) e = new ServerEventArgs(false, client);
            
            // Need a local success variable in case base Authentication changes e.Success from true
            var localSuccess = false;

            while (true)
            {
                WritePacket(MakePacket("Please enter a nickname:"), client);
                while (!client.HasQueuedReadMessages) Thread.Sleep(1);
                
                nick = (client.ReadPacket() as ChatPacket).Message;
                if (IsNickAvailable(nick))
                {
                    localSuccess = e.Success = true;
                    nickTable[client] = nick;

                    WritePacket(MakePacket("Successfully registered nickname: {0}".format(nick)), client);

                    // Replace failed attempt message with success
                    e.Parameters.Remove(nickInUseKey);
                    e.Parameters["Server:AuthSucceed:Data:Nick"] = nick;
                }
                else
                {
                    WritePacket(MakePacket("Sorry, that name is not available."), client);
                    e.Parameters[nickInUseKey] = nick;
                    log.Info(nickInUseKey + ":<{0}>".format(nick));
                }
                
                base.Authenticate(client, e);
                if (localSuccess) break;
            }
        }

        public override void ReceivePacket(Packet packet, Client client)
        {
            var nick = nickTable[client];
            var cPacket = packet as ChatPacket;
            if(String.IsNullOrEmpty(cPacket.Message)) return;
            SendPacket(MakePacket("{0}: {1}".format(nick, cPacket.Message)));
        }

        ChatPacket MakePacket(string msg)
        {
            var packet = new ChatPacket();
            packet.Message = msg;
            packet.From = "";
            packet.To = "";
            return packet;
        }
    }
}
