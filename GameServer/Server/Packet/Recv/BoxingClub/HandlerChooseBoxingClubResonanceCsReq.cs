using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util; // 引用 Logger 所在的命名空间
using EggLink.DanhengServer.GameServer.Game.BoxingClub; // 引用 BoxingClubManager 所在的命名空间

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.ChooseBoxingClubResonanceCsReq)]
public class HandlerChooseBoxingClubResonanceCsReq : Handler
{
    // 获取当前类的专用 Logger
    private static readonly Logger _log = Logger.GetByClassName();

    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = ChooseBoxingClubResonanceCsReq.Parser.ParseFrom(data);
        var manager = connection.Player?.BoxingClubManager;

        if (manager == null) return;

        // 1. 调用你写的业务逻辑：推进轮次并生成快照
        var snapshot = manager.ProcessChooseResonance(req.ChallengeId);

        if (snapshot != null)
        {
            // 2. 发送响应 (4269)
            await connection.SendPacket(new PacketChooseBoxingClubResonanceScRsp(0, snapshot));

            // 3. 发送更新通知 (4244) - 驱动客户端 UI 进入“准备匹配下一场”的状态
            await connection.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));
            
            // 修正错误：通过类名访问静态变量 EnableLog
            if (BoxingClubManager.EnableLog) 
            {
                _log.Debug($"[Boxing] Resonance processed. Challenge: {req.ChallengeId}, Next Progress: {snapshot.HNPEAPPMGAA}");
            }
        }
    }
}
