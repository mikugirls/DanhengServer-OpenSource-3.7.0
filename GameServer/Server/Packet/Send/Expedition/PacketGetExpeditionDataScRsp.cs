using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketGetExpeditionDataScRsp : BasePacket
{
    public PacketGetExpeditionDataScRsp(PlayerInstance player) : base(CmdIds.GetExpeditionDataScRsp)
    {
        var proto = new GetExpeditionDataScRsp
        {
            Retcode = 0
        };

        [cite_start]// 从 ExpeditionManager 的数据库模型中提取数据并填充 [cite: 3]
        // 这里使用了我们之前在 ExpeditionData 中定义的 ToProto() 转换方法
        proto.ExpeditionList.AddRange(player.ExpeditionManager.Data.ToProto());

        // 设置协议数据
        SetData(proto);
    }
}
