using EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Lineup;

[Opcode(CmdIds.SetLineupNameCsReq)]
public class HandlerSetLineupNameCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        // 1. 解析客户端请求
        var req = SetLineupNameCsReq.Parser.ParseFrom(data);

        // 2. 这里的 if 判空是消除 CS8602 警告的关键
        // 只有确保 Player 不为空时才执行后续逻辑
        if (connection.Player?.LineupManager != null)
        {
            // 3. 执行改名逻辑（内部会完成：数据库取值赋值 -> SaveDatabaseType）
            await connection.Player.LineupManager.SetLineupName((int)req.Index, req.Name);

            // 4. 发送响应包（仿照你提供的 BasePacket 风格）
            // 这里将 req.Index 和 req.Name 传给回包，保证客户端收到的就是数据库确认过的值
            await connection.SendPacket(new PacketSetLineupNameScRsp(req.Index, req.Name));
        }
    }
}