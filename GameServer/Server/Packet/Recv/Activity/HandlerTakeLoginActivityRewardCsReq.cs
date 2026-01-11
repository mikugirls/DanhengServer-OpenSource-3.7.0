using EggLink.DanhengServer.GameServer.Server;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Activity;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Activity;

[Opcode(CmdIds.TakeLoginActivityRewardCsReq)]
public class HandlerTakeLoginActivityRewardCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = TakeLoginActivityRewardCsReq.Parser.ParseFrom(data);
        var player = connection.Player;

        if (player?.ActivityManager == null) return;

        // 【关键改动】：解包 4 个参数，获取服务器纠偏后的 finalId
        var (rewardProto, panelId, retcode, finalId) = await player.ActivityManager.TakeLoginReward(req.Id, req.TakeDays);

        // 【关键改动】：发送 Packet 时传入 finalId (如 10018)，而不是原始的 req.Id (如 1003)
        await connection.SendPacket(new PacketTakeLoginActivityRewardScRsp(finalId, req.TakeDays, retcode, rewardProto, panelId));
    }
}