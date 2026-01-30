using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketTakeExpeditionRewardScRsp : BasePacket
{
    // 构造函数：必须没有返回类型（如 void/int），名称必须与类名完全一致
    public PacketTakeExpeditionRewardScRsp(uint expeditionId, ItemList reward, ItemList extraReward) 
        : base(CmdIds.TakeExpeditionRewardScRsp)
    {
        var proto = new TakeExpeditionRewardScRsp
        {
            Retcode = 0,
            ExpeditionId = expeditionId,
            Reward = reward,
            ExtraReward = extraReward // 这里存放角色加成带来的额外奖励
        };

        SetData(proto);
    }
}
