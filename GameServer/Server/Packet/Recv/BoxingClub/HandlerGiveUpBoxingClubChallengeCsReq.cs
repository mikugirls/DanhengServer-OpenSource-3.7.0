using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto; // 必须，解析 GiveUpBoxingClubChallengeCsReq
using EggLink.DanhengServer.GameServer.Game.BoxingClub; // 必须，访问 BoxingClubManager
using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub; // 必须，发送 ScRsp 和 ScNotify

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.GiveUpBoxingClubChallengeCsReq)]
public class HandlerGiveUpBoxingClubChallengeCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = GiveUpBoxingClubChallengeCsReq.Parser.ParseFrom(data);
        var manager = connection.Player?.BoxingClubManager;

        if (manager == null) return;

        // PCPDFJHDJCC == true 代表彻底放弃，归零进度
        var snapshot = manager.ProcessGiveUpChallenge(req.ChallengeId, req.PCPDFJHDJCC);

        // 返回 ScRsp (4252)
        await connection.SendPacket(new PacketGiveUpBoxingClubChallengeScRsp(0, snapshot));
        
        // 发送 ScNotify (4244) 同步客户端 UI 状态
        await connection.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));
    }
}
