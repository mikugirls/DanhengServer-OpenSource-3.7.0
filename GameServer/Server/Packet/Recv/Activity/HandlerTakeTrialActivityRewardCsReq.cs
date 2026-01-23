using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database.Activity;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Activity;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.PlayerSync;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Scene;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Database;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Activity;

[Opcode(CmdIds.TakeTrialActivityRewardCsReq)]
public class HandlerTakeTrialActivityRewardCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = TakeTrialActivityRewardCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;

        // 1. 获取试用关卡配置
        if (!GameData.AvatarDemoConfigData.TryGetValue((int)req.StageId, out var stage)) return;

        // 2. 获取奖励数据
        if (!GameData.RewardDataData.TryGetValue(stage.RewardID, out var reward)) return;

        // 3. 构造奖励列表 (手动创建用于显示的列表)
        var rewardItems = reward.GetItems().Select(x => new ItemData { ItemId = x.Item1, Count = x.Item2 }).ToList();
        
        // 如果包含星琼，加入列表。AddItems 内部会处理 Player.Data.Hcoin 的逻辑
        if (reward.Hcoin > 0)
        {
            rewardItems.Add(new ItemData { ItemId = 1, Count = (int)reward.Hcoin });
        }

        // 4. 执行批量添加
        // 修复 CS0815：不再尝试接收 AddItems 的返回值，因为它返回的是 ValueTask (void)
        await player.InventoryManager!.AddItems(rewardItems, notify: true);

        // 5. 更新活动数据中的领取状态
        var trialData = player.ActivityManager!.Data.TrialActivityData;
        var activityIndex = trialData.Activities.FindIndex(x => x.StageId == req.StageId);
        if (activityIndex != -1)
        {
            trialData.Activities[activityIndex] = new TrialActivityResultData
            {
                StageId = (int)req.StageId,
                TakenReward = true
            };
        }

        // 6. 标记数据库持久化
        if (!DatabaseHelper.ToSaveUidList.Contains(player.Uid))
            DatabaseHelper.ToSaveUidList.Add(player.Uid);

        // 7. 同步玩家属性 (刷新星琼顶栏)
        // 因为 AddItems 内部对虚拟物品使用静默模式，这里必须补发全量包
        await player.SendPacket(new PacketPlayerSyncScNotify(player.ToProto()));

        // 8. 发送领取结果响应
        await connection.SendPacket(new PacketTakeTrialActivityRewardScRsp(req.StageId, rewardItems));

        // 9. 同步活动全量状态，彻底防止客户端 UI 卡死
        await player.ActivityManager.SyncTrialActivity();
    }
}
