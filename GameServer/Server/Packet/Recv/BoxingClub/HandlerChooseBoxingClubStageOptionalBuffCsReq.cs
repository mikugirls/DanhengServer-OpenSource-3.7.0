using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.ChooseBoxingClubStageOptionalBuffCsReq)]
public class HandlerChooseBoxingClubStageOptionalBuffCsReq : Handler
{
    private static readonly Logger _log = Logger.GetByClassName();

    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        // 1. 解析协议 (4292)
        var req = ChooseBoxingClubStageOptionalBuffCsReq.Parser.ParseFrom(data);

        // 2. 消除 CS8602 警告：安全提取 BoxingClubManager
        if (connection.Player?.BoxingClubManager is not { } manager)
        {
            _log.Warn("无法处理 4292 请求：玩家未登录或 Manager 未就绪。");
            return;
        }

        // 3. 执行间隙 Buff 选择与下一轮怪物匹配
        // 注意：根据你的 Proto 定义，字段名可能是 FMGMAIEGOFP
        var snapshot = manager.ProcessOptionalBuff(req.ChallengeId, req.FMGMAIEGOFP);

        if (snapshot != null)
        {
            // 4. 先发送响应包 (4237)
            await connection.SendPacket(new PacketChooseBoxingClubStageOptionalBuffScRsp(0, snapshot));
            
            // 5. 后发送同步通知 (4253)，触发客户端 UI 刷新和转盘动画
            await connection.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));

            _log.Debug($"[Boxing] 间隙Buff选择成功: {req.FMGMAIEGOFP}, 准备开启第 {manager.ChallengeInstance?.CurrentRoundIndex + 1} 轮。");
        }
        else
        {
            _log.Error($"[Boxing] ProcessOptionalBuff 失败：无法为挑战 {req.ChallengeId} 生成数据快照。");
        }
    }
}