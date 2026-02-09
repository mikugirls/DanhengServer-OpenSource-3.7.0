using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Kcp; // 修复 BasePacket 找不到的问题

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Shop;
public class PacketTakeCityShopRewardScRsp : BasePacket
{
    // 修改构造函数，支持直接传入 4 个参数
    public PacketTakeCityShopRewardScRsp(uint retcode, uint level, uint shopId, ItemList? reward = null) 
        : base(CmdIds.TakeCityShopRewardScRsp)
    {
        var proto = new TakeCityShopRewardScRsp
        {
            Retcode = retcode,
            Level = level,
            ShopId = shopId,
            Reward = reward ?? new ItemList() 
        };

        SetData(proto);
    }
}

