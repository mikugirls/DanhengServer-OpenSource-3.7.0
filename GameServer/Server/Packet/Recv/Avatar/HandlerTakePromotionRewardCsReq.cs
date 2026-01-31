using EggLink.DanhengServer.GameServer.Server.Packet.Send.Avatar;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.PlayerSync;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Database;
namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Avatar;

[Opcode(CmdIds.TakePromotionRewardCsReq)]
public class HandlerTakePromotionRewardCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
{
    var req = TakePromotionRewardCsReq.Parser.ParseFrom(data);
    var player = connection.Player!;

    // 1. 获取角色
    var avatar = player.AvatarManager!.GetFormalAvatar((int)req.BaseAvatarId);
    if (avatar == null) return;

    // 2. 双重锁定检查 (防止无限领的核心)
    // 如果 TakeReward 返回 false，说明内存记录中已领，直接报错退出
    if (!avatar.TakeReward((int)req.Promotion))
    {
        // 可以发送一个错误回包，或者直接 return
        return; 
    }

    // 3. 校验晋阶等级
    if (avatar.Promotion < (int)req.Promotion) 
    {
        // 还没到这个等级，不能领（防止包体伪造）
        return; 
    }

    // 4. 【关键】立即同步数据库
    // 必须在 AddItem 之前或紧随其后执行，确保数据库里的 Rewards 字段变成非零值
    DatabaseHelper.UpdateInstance(player.AvatarManager.AvatarData);

    // 5. 发放奖励 (星轨通票)
    await player.InventoryManager!.AddItem(101, 1, false);

    // 6. 同步 Proto 数据给客户端
    // 让客户端知道奖励已领，按钮才会变灰
    await connection.SendPacket(new PacketPlayerSyncScNotify(avatar));

    // 7. 返回响应
    await connection.SendPacket(new PacketTakePromotionRewardScRsp());
}
}