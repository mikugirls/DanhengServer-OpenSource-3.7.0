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

        // 核心逻辑全部交给 ShopService 处理
        var rsp = await player.ShopService!.TakeCityShopReward(req.ShopId, req.Level);
        
        // 发送 1586 响应包
        await connection.SendPacket(CmdIds.TakeCityShopRewardScRsp, rsp);

        // 如果领取成功，发送 1594 同步包刷新 UI 状态（按钮变灰）
        if (rsp.Retcode == 0)
        {
            await player.SendPacket(new PacketCityShopInfoScNotify(player, (int)req.ShopId));
        }
    }
}
