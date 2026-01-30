using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketTakeExpeditionRewardScRsp : BasePacket
{
    // 构造函数接收 派遣ID 和 奖励列表
   public class PacketTakeExpeditionRewardScRsp : BasePacket
{
    public PacketTakeExpeditionRewardScRsp(uint expeditionId, ItemList reward, ItemList extraReward) : base(CmdIds.TakeExpeditionRewardScRsp)
    {
        var proto = new TakeExpeditionRewardScRsp
        {
            Retcode = 0,
            ExpeditionId = expeditionId,
            Reward = reward,
            ExtraReward = extraReward // 填充额外奖励
        };
        SetData(proto);
    }
}
}
