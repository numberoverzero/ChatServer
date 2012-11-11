using Engine.DataStructures;
using Engine.Networking.Packets;

namespace ChatProgram
{
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
        public string Message;
        public bool Success;

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
}