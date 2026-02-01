using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.GetBoxingClubInfoCsReq)]
public class HandlerGetBoxingClubInfoCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var player = connection.Player!;

        // 调用刚才定义的 BoxingClub 命名空间下的逻辑
        var challengeList = player.BoxingClubManager?.GetChallengeList() ?? new List<FCIHIJLOMGA>();

        await connection.SendPacket(new PacketGetBoxingClubInfoScRsp((uint)Retcode.RetSucc, challengeList));
    }
}