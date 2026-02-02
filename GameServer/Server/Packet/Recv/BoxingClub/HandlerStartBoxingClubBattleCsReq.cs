using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Lineup;
using EggLink.DanhengServer.Database.Lineup;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.StartBoxingClubBattleCsReq)]
public class HandlerStartBoxingClubBattleCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = StartBoxingClubBattleCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;

        // 逻辑全部下沉到 Manager，直接返回构建好的战斗信息
        var sceneBattleInfo = player.BoxingClubManager.StartBattle(req.ChallengeId);

        if (sceneBattleInfo != null)
        {
            await connection.SendPacket(new PacketStartBoxingClubBattleScRsp((uint)Retcode.RetSucc, sceneBattleInfo));
        }
        else
        {
            await connection.SendPacket(new PacketStartBoxingClubBattleScRsp((uint)Retcode.RetBoxingClubChallengeNotOpen));
        }
    }
}
