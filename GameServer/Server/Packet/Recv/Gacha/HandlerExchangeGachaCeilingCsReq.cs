using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Gacha;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Gacha;

[Opcode(CmdIds.ExchangeGachaCeilingCsReq)]
public class HandlerExchangeGachaCeilingCsReq : Handler
{
   public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
{
    var req = ExchangeGachaCeilingCsReq.Parser.ParseFrom(data);
    var player = connection.Player!;
    var gachaData = player.GachaManager!.GachaData!;

    // --- 修复点：使用 ?? false 将 bool? 转换为 bool ---
    // 逻辑：如果数据为 null，则视为 false (未领取)
    if (gachaData.StandardCumulativeCount < 300 || (gachaData.IsStandardSelected ?? false))
    {
        await connection.SendPacket(new PacketExchangeGachaCeilingScRsp(3602));
        return;
    }

    var result = await player.InventoryManager!.AddItem((int)req.AvatarId, 1, notify: true, sync: true);

    if (result != null)
    {
        var rsp = new ExchangeGachaCeilingScRsp
        {
            Retcode = 0,
            GachaType = req.GachaType,
            AvatarId = req.AvatarId,
            GachaCeiling = new GachaCeiling
            {
                CeilingNum = (uint)gachaData.StandardCumulativeCount,
                IsClaimed = true
            }
        };

        if (result.ItemId == (int)req.AvatarId + 10000)
        {
            rsp.TransferItemList = new ItemList();
            rsp.TransferItemList.ItemList_.Add(new EggLink.DanhengServer.Proto.Item
            {
                ItemId = (uint)result.ItemId,
                Num = 1 
            });
        }

        // 标记为已领取
        gachaData.IsStandardSelected = true;
        DatabaseHelper.UpdateInstance(gachaData);

        await connection.SendPacket(new PacketExchangeGachaCeilingScRsp(rsp));
    }
}
}
