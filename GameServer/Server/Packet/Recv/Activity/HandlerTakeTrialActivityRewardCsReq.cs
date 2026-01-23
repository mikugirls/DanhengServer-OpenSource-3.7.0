using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database.Activity;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Activity;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.PlayerSync;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Scene;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Activity;

[Opcode(CmdIds.TakeTrialActivityRewardCsReq)]
public class HandlerTakeTrialActivityRewardCsReq : Handler
{
 public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = TakeTrialActivityRewardCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;

        GameData.AvatarDemoConfigData.TryGetValue((int)req.StageId, out var stage);
        if (stage != null)
        {
            GameData.RewardDataData.TryGetValue(stage.RewardID, out var reward);
            var itemList = new List<ItemData>();

            var rewardItems = reward?.GetItems() ?? new List<(int, int)>();
            foreach (var i in rewardItems)
            {
                // 修改点：将 sync 设为 false。
                // 这样 AddItem 内部就不会去加 Hcoin，也不会重复发同步包。
                var res = await player.InventoryManager!.AddItem(i.Item1, i.Item2, false, sync: false);
                if (res != null) itemList.Add(res);
            }

            var activities = player.ActivityManager!.Data.TrialActivityData.Activities;
            var activityIndex = activities.FindIndex(x => x.StageId == req.StageId);
            if (activityIndex != -1)
            {
                activities[activityIndex] = new TrialActivityResultData
                {
                    StageId = (int)req.StageId,
                    TakenReward = true
                };
            }

            
            // 保留所有原始发包逻辑
            await player.SendPacket(new PacketPlayerSyncScNotify(player.ToProto(), itemList));
            await player.SendPacket(new PacketScenePlaneEventScNotify(itemList));
            await connection.SendPacket(new PacketTakeTrialActivityRewardScRsp(req.StageId, itemList));
        }
    }
}
