using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.StartBoxingClubBattleCsReq)]
public class HandlerStartBoxingClubBattleCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = StartBoxingClubBattleCsReq.Parser.ParseFrom(data);
        
        // 1. 修复 CS8602: 使用安全访问符号 ? 替代 !
        var player = connection.Player;

        // 2. 增加安全检查：确保玩家和 Manager 已经就绪
        if (player?.BoxingClubManager == null)
        {
            await connection.SendPacket(new PacketStartBoxingClubBattleScRsp((uint)Retcode.RetBoxingClubChallengeNotOpen));
            return;
        }

        // 3. 修复 CS4014: 确保 await 战斗启动逻辑
        // 这样可以保证 PacketSceneEnterStageScRsp 先于 StartBoxingClubBattleScRsp 发出
        await player.BoxingClubManager.EnterBoxingClubStage(req.ChallengeId);
        
        // 4. 发送成功响应，告知客户端可以关闭活动 UI 容器
        await connection.SendPacket(new PacketStartBoxingClubBattleScRsp((uint)Retcode.RetSucc));
    }
}
