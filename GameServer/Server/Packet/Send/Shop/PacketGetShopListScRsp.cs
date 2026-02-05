using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;
namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Shop;
public class PacketGetShopListScRsp : BasePacket
{
    public PacketGetShopListScRsp(PlayerInstance player, uint shopType) : base(CmdIds.GetShopListScRsp)
    {
        var proto = new GetShopListScRsp { ShopType = shopType, Retcode = 0 };

        foreach (var item in GameData.ShopConfigData.Values)
        {
            if (item.ShopType == shopType && item.Goods.Count > 0)
            {
                int shopId = item.ShopID;
                var shopProto = new Proto.Shop
                {
                    ShopId = (uint)shopId,
                    CityLevel = 1,
                    EndTime = uint.MaxValue,
                    GoodsList = { item.Goods.Where(x => x.ItemID != 0).Select(g => g.ToProto()) }
                };

                // --- 调用 ShopService 处理数据 ---
                if (GameData.CityShopConfigData.ContainsKey(shopId))
                {
                    shopProto.CityLevel = player.ShopService!.CalculateCityLevel(shopId);
                    shopProto.CityExp = player.CityShopData!.GetExp(shopId);
                    shopProto.CityTakenLevelReward = player.CityShopData!.GetRewardMask(shopId);
                }

                proto.ShopList.Add(shopProto);
            }
        }
        SetData(proto);
    }
}
