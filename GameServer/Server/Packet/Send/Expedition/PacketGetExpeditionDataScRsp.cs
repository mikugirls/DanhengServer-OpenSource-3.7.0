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
            // 对应 team_count: 告诉客户端当前解锁了多少个派遣槽位
            TotalExpeditionCount = (uint)player.ExpeditionManager.GetUnlockedExpeditionSlots()
        };

        if (player.ExpeditionData != null)
        {
            // 修正：字段名必须匹配协议中的 ExpeditionInfo
            proto.ExpeditionInfo.AddRange(player.ExpeditionData.ToProto());

            // 对应 unlocked_expedition_id_list: 下发已解锁的派遣点 ID 列表
            // 暂时下发所有配置 ID 以防客户端界面显示“锁定”
            proto.JFJPADLALMD.AddRange(GameData.ExpeditionDataData.Keys.Select(x => (uint)x));
        }

        SetData(proto);
    }
}
