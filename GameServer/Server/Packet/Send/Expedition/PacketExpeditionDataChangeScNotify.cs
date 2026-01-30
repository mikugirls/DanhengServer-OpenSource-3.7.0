using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketExpeditionDataChangeScNotify : BasePacket
{
    // 构造函数接收完整的 Proto 对象
    public PacketExpeditionDataChangeScNotify(ExpeditionDataChangeScNotify proto) 
        : base(CmdIds.ExpeditionDataChangeScNotify)
    {
        SetData(proto);
    }
}
