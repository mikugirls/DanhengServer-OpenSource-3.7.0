using EggLink.DanhengServer.GameServer.Server.Packet.Send.Item;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Item;

[Opcode(CmdIds.ComposeItemCsReq)]
public class HandlerComposeItemCsReq : Handler
{
   public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
{
    var req = ComposeItemCsReq.Parser.ParseFrom(data);
    var player = connection.Player!;

    // --- 修正 1：合并两个列表的数据 ---
    var costData = new List<ItemCost>();
    
    // 普通合成消耗
    if (req.ComposeItemList?.ItemList != null)
        costData.AddRange(req.ComposeItemList.ItemList);
        
    // 转换/置换消耗 (行迹转换的数据在这里)
    if (req.ConvertItemList?.ItemList != null)
        costData.AddRange(req.ConvertItemList.ItemList);

    // 调用 Manager 执行逻辑
    var item = await player.InventoryManager!.ComposeItem((int)req.ComposeId, (int)req.Count, costData);
    
    if (item == null)
    {
        await connection.SendPacket(new PacketComposeItemScRsp());
        return;
    }

    // --- 修正 2：在返回结果前，强制触发 UI 状态更新 ---
    // 发送限额更新通知 (使用你之前提供的 ComposeLimitNumUpdateNotify)
    // 即使次数没变，发这个包也会让合成台 UI 重新渲染材料列表
    //await player.SendPacket(new PacketComposeLimitNumUpdateNotify((int)req.ComposeId));

    // 最后发送成功响应，弹出获得物品界面
    await connection.SendPacket(new PacketComposeItemScRsp(req.ComposeId, req.Count, item));
}
}