using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Expedition;

[Opcode(CmdIds.TakeExpeditionRewardCsReq)]
public class HandlerTakeExpeditionRewardCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = TakeExpeditionRewardCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;

        // 调用 Manager 处理领奖逻辑
        await player.ExpeditionManager!.TakeExpeditionReward(req.ExpeditionId);
    }
}
