using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;

public class PacketBoxingClubRewardScNotify : BasePacket
{
    // 模仿你的模板，传入构造好的 BoxingClubRewardScNotify 对象
    public PacketBoxingClubRewardScNotify(BoxingClubRewardScNotify notify) : base(CmdIds.BoxingClubRewardScNotify)
    {
        SetData(notify);
    }
}
