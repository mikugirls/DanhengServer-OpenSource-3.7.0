using EggLink.DanhengServer.GameServer.Server.Packet.Send.Shop;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Data; // 确保引用了 GameData

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Shop;

[Opcode(CmdIds.GetShopListCsReq)]
public class HandlerGetShopListCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = GetShopListCsReq.Parser.ParseFrom(data);
        var player = connection.Player;
        if (player == null) return;

        // 1. 发送商店列表响应
        await connection.SendPacket(new PacketGetShopListScRsp(player, req.ShopType));

        // 2. 【核心修复】找到该分类下所有的城市商店并发送 Notify
        // 遍历所有城市商店配置
        foreach (var cityConfig in GameData.CityShopConfigData.Values)
        {
            // 检查这个城市商店是否属于玩家当前请求的这个 ShopType
            // 注意：这里需要根据你的配置结构，确认 ShopID 是否匹配
            // 如果你确定 401 就在这个分类里，也可以简单粗暴地判断
            if (GameData.ShopConfigData.TryGetValue(cityConfig.ShopID, out var shopConfig))
            {
                // 如果该商店的分类与请求一致，立刻同步城市经验
                if (shopConfig.ShopType == req.ShopType)
                {
                    await connection.SendPacket(new PacketCityShopInfoScNotify(player, cityConfig.ShopID));
                    
                    if (Util.GlobalDebug.EnableVerboseLog)
                        Console.WriteLine($"[SHOP_INIT] 自动同步城市商店信息: ShopID {cityConfig.ShopID} | Type {req.ShopType}");
                }
            }
        }
    }
}
