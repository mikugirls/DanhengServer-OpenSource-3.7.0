using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;

public class PacketBoxingClubChallengeUpdateScNotify : BasePacket
{
    public PacketBoxingClubChallengeUpdateScNotify(FCIHIJLOMGA challenge) : base(CmdIds.BoxingClubChallengeUpdateScNotify)
    {
        SetData(new BoxingClubChallengeUpdateScNotify
        {
            Challenge = challenge 
        });
    }
}
