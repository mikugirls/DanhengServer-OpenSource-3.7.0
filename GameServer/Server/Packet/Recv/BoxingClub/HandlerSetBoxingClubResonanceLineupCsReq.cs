using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Kcp; // <--- 关键：CmdIds 定义在这里
namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.SetBoxingClubResonanceLineupCsReq)]
public class HandlerSetBoxingClubResonanceLineupCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = SetBoxingClubResonanceLineupCsReq.Parser.ParseFrom(data);
        var manager = connection.Player?.BoxingClubManager;

        if (manager == null) return;

        // 1. 调用 Manager 中的业务逻辑同步阵容并获取快照
        // 注意：MDLACHDKMPH 是 Repeated 字段，直接传入进行处理
        var snapshot = manager.ProcessResonanceLineup(req.ChallengeId, req.MDLACHDKMPH);

        if (snapshot != null)
        {
            // 2. 构造响应包 (ScRsp)
            var rsp = new SetBoxingClubResonanceLineupScRsp
            {
                Retcode = 0,
                Challenge = snapshot // 对应 Proto 中的 challenge 字段 (Tag 12)
            };

            // 3. 发送给客户端
            await connection.SendPacket(new PacketSetBoxingClubResonanceLineupScRsp(0, snapshot));
            
            // 4. (可选) 同时发送一个 Notify 确保全量更新，防止 UI 状态没刷过来
            await connection.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));
        }
    }
}