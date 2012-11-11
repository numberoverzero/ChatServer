using System;
using System.Collections.Generic;
using System.Net;
using Engine.DataStructures;
using Engine.Networking;
using Engine.Networking.Packets;
using Engine.Utility;

namespace ChatProgram
{
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
}