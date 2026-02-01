using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;

public class PacketMatchBoxingClubOpponentScRsp : BasePacket
{
    public PacketMatchBoxingClubOpponentScRsp(uint retcode, FCIHIJLOMGA challenge) : base(CmdIds.MatchBoxingClubOpponentScRsp)
    {
        var proto = new MatchBoxingClubOpponentScRsp
        {
            Retcode = retcode
        };

        // 如果匹配成功，将包含怪物组 ID (HJMGLEMJHKG) 的对象填充进去
        if (challenge != null)
        {
            proto.Challenge = challenge;
        }

        SetData(proto);
    }
}