using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketTakeExpeditionRewardScRsp : BasePacket
{
    // 构造函数接收 派遣ID 和 奖励列表
    public PacketTakeExpeditionRewardScRsp(uint expeditionId, ItemList reward) : base(CmdIds.TakeExpeditionRewardScRsp)
    {
        var proto = new TakeExpeditionRewardScRsp
        {
            Retcode = 0,
            ExpeditionId = expeditionId,
            Reward = reward,
            // 如果有额外奖励逻辑可以填充 ExtraReward，暂时填空
            ExtraReward = new ItemList() 
        };

        SetData(proto);
    }
}
