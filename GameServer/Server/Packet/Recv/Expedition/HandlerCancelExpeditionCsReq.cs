using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Expedition;

[Opcode(CmdIds.CancelExpeditionCsReq)]
public class HandlerCancelExpeditionCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = CancelExpeditionCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;

        // 调用 Manager 执行取消逻辑
        await player.ExpeditionManager!.CancelExpedition(req.ExpeditionId);
    }
}
