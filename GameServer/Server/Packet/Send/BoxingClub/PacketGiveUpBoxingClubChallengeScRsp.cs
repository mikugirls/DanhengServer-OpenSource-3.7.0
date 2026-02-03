using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;

public class PacketGiveUpBoxingClubChallengeScRsp : BasePacket
{
    /// <summary>
    /// 构造放弃挑战的响应包 (CmdId: 4252)
    /// </summary>
    /// <param name="retcode">错误码，0 为成功</param>
    /// <param name="challenge">重置后的挑战快照 (FCIHIJLOMGA)</param>
    public PacketGiveUpBoxingClubChallengeScRsp(uint retcode, FCIHIJLOMGA challenge) : base(CmdIds.GiveUpBoxingClubChallengeScRsp)
    {
        SetData(new GiveUpBoxingClubChallengeScRsp
        {
            Retcode = retcode,
            Challenge = challenge 
        });
    }
}
