using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Shop;

public class PacketCityShopInfoScNotify : BasePacket
{
    public PacketCityShopInfoScNotify(PlayerInstance player, int shopId) : base(CmdIds.CityShopInfoScNotify)
{
    uint currentExp = player.CityShopData?.GetExp(shopId) ?? 0;
    uint physicalMax = player.ShopService!.GetPhysicalMaxLevel(shopId, currentExp);
    ulong takenMask = player.CityShopData?.GetRewardMask(shopId) ?? 0;

    var notify = new CityShopInfoScNotify
    {
        ShopId = (uint)shopId,
        Exp = currentExp,
        // 维持 Level = physicalMax + 1 方案，确保 UI 判定所有达标等级为“已完成”
        Level = physicalMax + 1, 
        TakenLevelReward = takenMask // 此时领了1级，这里发的就是 2 (二进制 10)
    };

    SetData(notify);
}
}
