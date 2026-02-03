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

    // 根据 Proto 源码，Tag 12 (LLFOFPNDAFG) 就是选中的 Buff ID
    var snapshot = manager.ProcessChooseResonance(req.ChallengeId, req.LLFOFPNDAFG);

    if (snapshot != null)
    {
        await connection.SendPacket(new PacketChooseBoxingClubResonanceScRsp(0, snapshot));
        await connection.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));
        
        if (BoxingClubManager.EnableLog) 
        {
            _log.Debug($"[Boxing] Resonance {req.LLFOFPNDAFG} saved. Challenge: {req.ChallengeId}");
        }
    }
}
   
}
