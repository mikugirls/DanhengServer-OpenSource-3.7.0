using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Kcp;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;

public class PacketStartBoxingClubBattleScRsp : BasePacket
{
    // 进战斗成功时使用的构造函数
    public PacketStartBoxingClubBattleScRsp(uint challengeId, SceneBattleInfo battleInfo) : base(CmdIds.StartBoxingClubBattleScRsp)
    {
        SetData(new StartBoxingClubBattleScRsp
        {
            Retcode = 0,
            ChallengeId = challengeId,
            BattleInfo = battleInfo
        });
    }

    // 失败或极简回复时使用的构造函数
    public PacketStartBoxingClubBattleScRsp(uint retcode) : base(CmdIds.StartBoxingClubBattleScRsp)
    {
        SetData(new StartBoxingClubBattleScRsp
        {
            Retcode = retcode
        });
    }
}