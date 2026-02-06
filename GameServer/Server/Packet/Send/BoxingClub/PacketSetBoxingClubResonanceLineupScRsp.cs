using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Kcp;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;

// 继承 BasePacket 而不是 ScPacket
public class PacketSetBoxingClubResonanceLineupScRsp : BasePacket
{
    // 构造函数：接收 retcode 和快照实例
    public PacketSetBoxingClubResonanceLineupScRsp(uint retcode, FCIHIJLOMGA snapshot) 
        : base(CmdIds.SetBoxingClubResonanceLineupScRsp)
    {
        // 使用 SetData 注入 Proto 消息
        SetData(new SetBoxingClubResonanceLineupScRsp
        {
            Retcode = retcode,
            Challenge = snapshot // 对应 Proto 中的 challenge 字段 (Tag 12)
        });
    }

    // 纯 Retcode 重载（用于处理错误）
    public PacketSetBoxingClubResonanceLineupScRsp(uint retcode) 
        : base(CmdIds.SetBoxingClubResonanceLineupScRsp)
    {
        SetData(new SetBoxingClubResonanceLineupScRsp
        {
            Retcode = retcode
        });
    }
}