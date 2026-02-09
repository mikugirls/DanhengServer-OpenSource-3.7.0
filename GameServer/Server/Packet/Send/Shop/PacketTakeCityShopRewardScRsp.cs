using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Kcp; // 修复 BasePacket 找不到的问题

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Shop;

public class PacketTakeCityShopRewardScRsp : BasePacket
{
    public PacketTakeCityShopRewardScRsp(TakeCityShopRewardScRsp rspData) : base(CmdIds.TakeCityShopRewardScRsp)
    {
        SetData(rspData);
    }
}
