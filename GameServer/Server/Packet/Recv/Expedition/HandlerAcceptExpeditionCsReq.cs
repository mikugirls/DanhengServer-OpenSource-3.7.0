using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Expedition;

[Opcode(CmdIds.AcceptExpeditionCsReq)]
public class HandlerAcceptExpeditionCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        // 1. 解析客户端发来的开始派遣请求
        var req = AcceptExpeditionCsReq.Parser.ParseFrom(data);
        
        // 2. 获取当前连接的玩家实例
        var player = connection.Player!;

        // 3. 调用 Manager 中已经写好的 Accept 逻辑（负责校验、服务器授时、存入数据库）
        await player.ExpeditionManager!.AcceptExpedition(req);
    }
}
