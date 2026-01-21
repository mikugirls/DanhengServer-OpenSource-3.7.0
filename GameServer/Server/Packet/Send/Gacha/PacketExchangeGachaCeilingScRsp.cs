using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Gacha;

public class PacketExchangeGachaCeilingScRsp : BasePacket
{
    // 成功时：传入构造好的完整的 proto 对象
    public PacketExchangeGachaCeilingScRsp(ExchangeGachaCeilingScRsp proto) : base(CmdIds.ExchangeGachaCeilingScRsp)
    {
        SetData(proto);
    }

    // 失败时：简易构造函数，仅返回错误码 (Retcode)
    public PacketExchangeGachaCeilingScRsp(uint retcode) : base(CmdIds.ExchangeGachaCeilingScRsp)
    {
        SetData(new ExchangeGachaCeilingScRsp
        {
            Retcode = retcode
        });
    }
}
