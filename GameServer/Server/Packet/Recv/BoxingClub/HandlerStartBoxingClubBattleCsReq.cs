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

        // 调用 Manager 内部封装的位面进入逻辑
        // 内部会计算 StageID = EventID * 10 + WorldLevel
        // 并通过 PacketSceneEnterStageScRsp 发送 BattleInfo
        await player.BoxingClubManager.EnterBoxingClubStage(req.ChallengeId);
        
        // 注意：StartBoxingClubBattleScRsp 也要发一个，用来关掉 UI 的 Loading 或触发逻辑
        await connection.SendPacket(new PacketStartBoxingClubBattleScRsp((uint)Retcode.RetSucc));
    }
}
