using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.ChooseBoxingClubResonanceCsReq)]
public class HandlerChooseBoxingClubResonanceCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = ChooseBoxingClubResonanceCsReq.Parser.ParseFrom(data);
        var manager = connection.Player?.BoxingClubManager;

        if (manager == null) return;

        // 执行业务逻辑
        var snapshot = manager.ProcessChooseResonance(req.ChallengeId);

        if (snapshot != null)
        {
            // 回复 ScRsp，UI 会刷新并允许进行下一轮 MatchRequest
            await connection.SendPacket(new PacketChooseBoxingClubResonanceScRsp(0, snapshot));
            // 3. 【核心修复】发送主动通知包 (ScNotify)
            // 这会强制客户端 UI 更新到 2/4 进度，并由于 snapshot.HLIBIJFHHPG 为空，
            // 触发客户端逻辑去请求 MatchBoxingClubOpponentCsReq。
            await connection.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));
            
            if (BoxingClubManager.EnableLog) 
                _log.Debug($"[Boxing] 已下发 4269 和 4244，当前进度推送到: {snapshot.HNPEAPPMGAA}");
        }
    }
}
