using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Data;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketGetExpeditionDataScRsp : BasePacket
{
  public PacketGetExpeditionDataScRsp(PlayerInstance player) : base(CmdIds.GetExpeditionDataScRsp)
{
    var proto = new GetExpeditionDataScRsp
    {
        Retcode = 0,
        // 使用 ?. 和 ?? 语法安全获取槽位数量
        TotalExpeditionCount = (uint)(player.ExpeditionManager?.GetUnlockedExpeditionSlots() ?? 2)
    };

    // 使用 ?. 确保只有在 Manager 和 Data 都不为空时才 AddRange
    if (player.ExpeditionData != null)
    {
        proto.ExpeditionInfo.AddRange(player.ExpeditionData.ToProto());

        // 下发已解锁的 ID 列表
        proto.JFJPADLALMD.AddRange(GameData.ExpeditionDataData.Keys.Select(x => (uint)x));
    }

    SetData(proto);
}
}
