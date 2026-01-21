using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Gacha;

public class PacketGetGachaCeilingScRsp : BasePacket
{
    public PacketGetGachaCeilingScRsp(GetGachaCeilingScRsp proto) : base(CmdIds.GetGachaCeilingScRsp)
    {
        SetData(proto);
    }
}
