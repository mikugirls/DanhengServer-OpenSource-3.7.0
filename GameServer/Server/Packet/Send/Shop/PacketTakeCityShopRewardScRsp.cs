using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Shop;

public class PacketTakeCityShopRewardScRsp : BasePacket
{
    /// <summary>
    /// 构造城市商店领奖响应包 (CmdId: 1586)
    /// </summary>
    /// <param name="retcode">错误码 (来自 Retcode.proto)</param>
    /// <param name="level">领取的等级</param>
    /// <param name="shopId">商店ID</param>
    /// <param name="reward">获得的道具列表</param>
    public PacketTakeCityShopRewardScRsp(uint retcode, uint level, uint shopId, ItemList reward = null) : base(CmdIds.TakeCityShopRewardScRsp)
    {
        // 这里的字段赋值会自动匹配你刚才修复的 Tag 3, 10, 12, 15
        var proto = new TakeCityShopRewardScRsp
        {
            Retcode = retcode,
            Level = level,
            ShopId = shopId,
            Reward = reward ?? new ItemList() // 如果没有奖励，传空列表防止客户端显示异常
        };

        SetData(proto);
    }
}
