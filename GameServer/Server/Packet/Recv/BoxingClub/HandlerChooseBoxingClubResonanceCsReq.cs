using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.GameServer.Game.BoxingClub; // 确保这一行存在

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.ChooseBoxingClubResonanceCsReq)]
public class HandlerChooseBoxingClubResonanceCsReq : Handler
{
    private static readonly Logger _log = Logger.GetByClassName();

    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = ChooseBoxingClubResonanceCsReq.Parser.ParseFrom(data);
        
        if (connection.Player?.BoxingClubManager is not { } manager) 
        {
            return;
        }

        var snapshot = manager.ProcessChooseResonance(req.ChallengeId, req.LLFOFPNDAFG);

        if (snapshot != null)
        {
            await connection.SendPacket(new PacketChooseBoxingClubResonanceScRsp(0, snapshot));
            await connection.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));
            
            // 简单处理日志，避免找不到类的错误
            _log.Debug($"[Boxing] Resonance {req.LLFOFPNDAFG} saved. Challenge: {req.ChallengeId}");
        }
    }
}