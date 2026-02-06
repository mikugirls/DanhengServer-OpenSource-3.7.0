using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Kcp;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;

public class PacketChooseBoxingClubStageOptionalBuffScRsp : BasePacket
{
    // 构造函数：回传 retcode 和快照
    public PacketChooseBoxingClubStageOptionalBuffScRsp(uint retcode, FCIHIJLOMGA snapshot) 
        : base(CmdIds.ChooseBoxingClubStageOptionalBuffScRsp) // 确保 CmdIds 里有 4237
    {
        SetData(new ChooseBoxingClubStageOptionalBuffScRsp
        {
            Retcode = retcode,
            Challenge = snapshot // 对应 Proto 里的快照字段
        });
    }
}