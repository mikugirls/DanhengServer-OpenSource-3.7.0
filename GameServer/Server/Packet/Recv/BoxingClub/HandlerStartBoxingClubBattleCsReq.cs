using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Lineup;
using EggLink.DanhengServer.Database.Lineup;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.StartBoxingClubBattleCsReq)]
public class HandlerStartBoxingClubBattleCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
       
    }
}