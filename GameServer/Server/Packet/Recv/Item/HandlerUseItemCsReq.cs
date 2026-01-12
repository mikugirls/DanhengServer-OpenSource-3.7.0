using EggLink.DanhengServer.GameServer.Server.Packet.Send.Item;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Item;

[Opcode(CmdIds.UseItemCsReq)]
public class HandlerUseItemCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        // 1. 解析客户端请求
        var req = UseItemCsReq.Parser.ParseFrom(data);

        // 2. 调用 InventoryManager 的 UseItem 逻辑
        // 注意：这里传入了 req.OptionalRewardId，这是自选礼包（星海宝藏）的关键
        // result 现在是一个包含三个元素的元组: (Retcode ret, List<ItemData>? items, uint formulaId)
        var result = await connection.Player!.InventoryManager!.UseItem(
            (int)req.UseItemId, 
            (int)req.UseItemCount,
            (int)req.BaseAvatarId,
            req.OptionalRewardId // <--- 新增：传入自选奖励ID
        );

        // 3. 构建响应包并发送
        // 使用你之前修改过的 PacketUseItemScRsp 构造函数
        // 参数顺序：retCode, itemId, count, returnItems, formulaId
        await connection.SendPacket(new PacketUseItemScRsp(
            result.ret,          // 错误码
            req.UseItemId,       // 使用的物品ID
            req.UseItemCount,    // 使用数量
            result.returnItems,  // 获得的奖励列表（自选的光锥在这里）
            result.formulaId     // 解锁的配方ID（触发客户端配方解锁动画）
        ));
    }
}