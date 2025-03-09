using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class VscodeIntegration
{
    private static readonly Uri vscodeUri = new Uri("ws://localhost:3001");

    public static async Task SendCodeToVscode(string code)
    {
        using (var client = new ClientWebSocket())
        {
            await client.ConnectAsync(vscodeUri, CancellationToken.None);
            byte[] bytes = Encoding.UTF8.GetBytes(code);
            await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
    }
}
