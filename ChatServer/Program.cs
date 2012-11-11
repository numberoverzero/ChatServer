using System.Net;
using System.Windows.Forms;

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
}