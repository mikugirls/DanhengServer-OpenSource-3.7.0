using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Data;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;
[Opcode(CmdIds.MatchBoxingClubOpponentCsReq)]
public class HandlerMatchBoxingClubOpponentCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = MatchBoxingClubOpponentCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;
        var manager = player.BoxingClubManager;

        if (manager == null) return;

        // 直接把整个 REQ 传进去处理
        var challengeSnapshot = manager.ProcessMatchRequest(req);

        // 发送响应包
        await connection.SendPacket(new PacketMatchBoxingClubOpponentScRsp(0, challengeSnapshot));
    }
}
