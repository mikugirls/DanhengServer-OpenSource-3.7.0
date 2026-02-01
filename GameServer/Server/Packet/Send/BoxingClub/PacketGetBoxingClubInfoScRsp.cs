using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;

public class PacketGetBoxingClubInfoScRsp : BasePacket
{
    public PacketGetBoxingClubInfoScRsp(uint retcode, List<FCIHIJLOMGA> challengeList) : base(CmdIds.GetBoxingClubInfoScRsp)
    {
        var proto = new GetBoxingClubInfoScRsp
        {
            Retcode = retcode
        };

        if (challengeList != null)
        {
            proto.ChallengeList.AddRange(challengeList);
        }

        SetData(proto);
    }
}