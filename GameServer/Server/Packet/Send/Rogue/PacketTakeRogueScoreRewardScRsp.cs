using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.GameServer.Game.Player;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Rogue;

public class PacketTakeRogueScoreRewardScRsp : BasePacket
{
    public PacketTakeRogueScoreRewardScRsp(PlayerInstance player, IEnumerable<uint> rewardIdList, List<(uint itemId, uint count)> displayRewards, int retcode = 0) 
        : base(CmdIds.TakeRogueScoreRewardScRsp)
    {
        var proto = new TakeRogueScoreRewardScRsp
        {
            Retcode = (uint)retcode,
            RogueScoreRewardInfo = player.RogueManager!.ToRewardProto(),
            PoolId = 1,
            Reward = new ItemList() 
        };

        if (displayRewards != null)
        {
            foreach (var item in displayRewards)
            {
                // 关键修正：使用全限定名 EggLink.DanhengServer.Proto.Item 避免 CS0118 错误
                proto.Reward.ItemList_.Add(new global::EggLink.DanhengServer.Proto.Item 
                { 
                    ItemId = item.itemId, 
                    Num = item.count 
                });
            }
        }

        this.SetData(proto);
    }
}