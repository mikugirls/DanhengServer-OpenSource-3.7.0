using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Shop;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Shop;

[Opcode(CmdIds.TakeCityShopRewardCsReq)] // 1583
public class HandlerTakeCityShopRewardCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = TakeCityShopRewardCsReq.Parser.ParseFrom(data);
        var player = connection.Player;
        if (player == null) return;

        // 1. 调用 Service 层逻辑
        var rspData = await player.ShopService!.TakeCityShopReward(req.ShopId, req.Level);
        
        // 2. 直接发送刚才写好的 Packet
        // 这里的 rspData 包含从 ShopService 返回的 retcode, level, shopId, reward
        await connection.SendPacket(new PacketTakeCityShopRewardScRsp(
            rspData.Retcode, 
            rspData.Level, 
            rspData.ShopId, 
            rspData.Reward
        ));

        // 3. 如果成功，别忘了同步商店状态
        if (rspData.Retcode == (uint)Retcode.RetSucc)
        {
            await player.SendPacket(new PacketCityShopInfoScNotify(player, (int)req.ShopId));
        }
    }
}
