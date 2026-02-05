using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Shop;

public class PacketCityShopInfoScNotify : BasePacket
{
    public PacketCityShopInfoScNotify(PlayerInstance player, int shopId) : base(CmdIds.CityShopInfoScNotify)
    {
        // 1. 获取当前总经验
        uint currentExp = player.CityShopData?.GetExp(shopId) ?? 0;
        
        // 2. 调用 ShopService 计算当前等级
        uint currentLevel = player.ShopService!.CalculateCityLevel(shopId);

        // 3. 获取已领取奖励的位掩码 (直接使用 ulong)
        ulong takenMask = player.CityShopData?.GetRewardMask(shopId) ?? 0;

        var notify = new CityShopInfoScNotify
        {
            ShopId = (uint)shopId,
            Exp = currentExp,
            Level = currentLevel,
            TakenLevelReward = takenMask // 对应 Proto 中的第 15 号字段 (ulong)
        };

        SetData(notify);
    }
}
