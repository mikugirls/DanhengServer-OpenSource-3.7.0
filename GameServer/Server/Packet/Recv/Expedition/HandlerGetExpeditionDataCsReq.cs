using EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;
using EggLink.DanhengServer.Kcp;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Expedition;

[Opcode(CmdIds.GetExpeditionDataCsReq)]
public class HandlerGetExpeditionDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var player = connection.Player!;

        // 仿照 Pet 处理模式，将玩家实例传入 ScRsp 包构造函数中
        await connection.SendPacket(new PacketGetExpeditionDataScRsp(player));
    }
}
