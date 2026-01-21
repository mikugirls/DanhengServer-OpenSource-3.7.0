using EggLink.DanhengServer.GameServer.Server.Packet.Send.Gacha;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Database; // 必须引用这个来保存数据

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Gacha;

[Opcode(CmdIds.ExchangeGachaCeilingCsReq)]
public class HandlerExchangeGachaCeilingCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = ExchangeGachaCeilingCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;
        var gachaData = player.GachaManager!.GachaData!;

        // 1. 基础校验：抽数是否达标，是否已领取过
        if (gachaData.StandardCumulativeCount < 300 || gachaData.IsStandardSelected)
        {
            await connection.SendPacket(new PacketExchangeGachaCeilingScRsp(3602)); // 错误码
            return;
        }

        // 2. 调用 InventoryManager 的 AddItem。
        // 注意：AddItem 内部会判断已有角色则发星魂(ID+10000)，没有则发角色
        var result = await player.InventoryManager!.AddItem((int)req.AvatarId, 1, notify: true, sync: true);

        if (result != null)
        {
            // 3. 构造响应包
            var rsp = new ExchangeGachaCeilingScRsp
            {
                Retcode = 0,
                GachaType = req.GachaType,
                AvatarId = req.AvatarId,
                GachaCeiling = new GachaCeiling
                {
                    CeilingNum = (uint)gachaData.StandardCumulativeCount,
                    IsClaimed = true, // 标记为已领取
                    AvatarList = { player.GachaManager.GetGoldAvatars().Select(id => new GachaCeilingAvatar { AvatarId = (uint)id }) }
                }
            };

            // 4. 处理“转化列表” (TransferItemList)
            // 如果 AddItem 返回的 ID 是 1xxxx (星魂)，说明发生了转化
            if (result.ItemId == (int)req.AvatarId + 10000)
            {
                rsp.TransferItemList = new ItemList();
                rsp.TransferItemList.ItemList_.Add(new Item
                {
                    ItemId = (uint)result.ItemId,
                    Number = 1
                });
            }

            // 5. 更新并保存状态
            gachaData.IsStandardSelected = true;
            player.GachaManager.Save();

            await connection.SendPacket(new PacketExchangeGachaCeilingScRsp(rsp));
        }
    }
}
