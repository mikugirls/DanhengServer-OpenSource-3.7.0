using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketGetExpeditionDataScRsp : ScPacket
{
    public PacketGetExpeditionDataScRsp(PlayerInstance player) : base(CmdIds.GetExpeditionDataScRsp)
    {
        var rsp = new GetExpeditionDataScRsp
        {
            Retcode = 0
        };

        // 从之前编写的数据库模型中提取数据并填入协议
        rsp.ExpeditionList.AddRange(player.ExpeditionManager.Data.ToProto());

        // 如果需要，可以同步当前已解锁的槽位数量
        // rsp.UnlockedTeamCount = (uint)player.ExpeditionManager.GetUnlockedExpeditionSlots();

        this.Body = rsp;
    }
}
