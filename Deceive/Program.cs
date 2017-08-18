using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Xml;

namespace Deceive
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 8200);
            listener.Start();
            var incoming = listener.AcceptTcpClient();
            var sslIncoming = new SslStream(incoming.GetStream());

            var outgoing = new TcpClient("185.40.64.69", 5223);
            var sslOutgoing = new SslStream(outgoing.GetStream());
            sslOutgoing.AuthenticateAsClient("chat.euw1.lol.riotgames.com");

            var cert = new X509Certificate2(Properties.Resources.certificates);
            sslIncoming.AuthenticateAsServer(cert);

            new Thread(() =>
            {
                var byteCount = 0;
                var bytes = new byte[2048];

                do
                {
                    byteCount = sslIncoming.Read(bytes, 0, bytes.Length);

                    var content = Encoding.UTF8.GetString(bytes, 0, byteCount);
                    if (content.Contains("presence"))
                    {
                        var xml = new XmlDocument();
                        xml.LoadXml(content);

                        var presence = xml["presence"];
                        if (presence != null && presence.Attributes["to"] == null)
                        {
                            presence["show"].InnerText = "xa";

                            var status = new XmlDocument();
                            status.LoadXml(presence["status"].InnerText);
                            status["body"]["statusMsg"].InnerText = "";
                            status["body"]["gameStatus"].InnerText = "outOfGame";

                            presence["status"].InnerText = status.OuterXml;
                            content = presence.OuterXml;
                            sslOutgoing.Write(Encoding.UTF8.GetBytes(content));
                            continue;
                        }
                    }

                    sslOutgoing.Write(bytes, 0, byteCount);
                } while (byteCount != 0);
            }).Start();

            new Thread(() =>
            {
                var byteCount = 0;
                var bytes = new byte[2048];

                do
                {
                    byteCount = sslOutgoing.Read(bytes, 0, bytes.Length);
                    sslIncoming.Write(bytes, 0, byteCount);
                } while (byteCount != 0);
            }).Start();
        }
    }
}
