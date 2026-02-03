using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;

public class PacketChooseBoxingClubResonanceScRsp : BasePacket
{
    public PacketChooseBoxingClubResonanceScRsp(uint retcode, FCIHIJLOMGA? challenge) : base(CmdIds.ChooseBoxingClubResonanceScRsp)
    {
        SetData(new ChooseBoxingClubResonanceScRsp
        {
            Retcode = retcode,
            Challenge = challenge 
        });
    }
}
