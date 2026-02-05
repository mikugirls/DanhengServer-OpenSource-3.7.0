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

        // 1. 发送商店列表响应 (这里面你应该已经改好了，会带上真实的 CityLevel)
        await connection.SendPacket(new PacketGetShopListScRsp(player, req.ShopType));

        // 2. 【核心修复】检查是否为城市商店 (401, 402, 403)
        // 只有发送了这个 Notify，客户端左侧的“城市经验条”和“奖励预览”才会正确加载数据
        if (GameData.CityShopConfigData.ContainsKey((int)req.ShopType))
        {
            // 发送我们在上一阶段定义的通知包
            await connection.SendPacket(new PacketCityShopInfoScNotify(player, (int)req.ShopType));
        }
    }
}
