using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketExpeditionDataChangeScNotify : BasePacket
{
    public PacketExpeditionDataChangeScNotify(uint totalCount, List<ExpeditionInfoInstance> expeditions) 
        : base(CmdIds.ExpeditionDataChangeScNotify)
    {
        var proto = new ExpeditionDataChangeScNotify
        {
            TotalExpeditionCount = totalCount,
            // 转换内存中的实例为协议格式
            ExpeditionInfo = { expeditions.Select(x => x.ToProto()) }
        };

        SetData(proto);
    }
}
