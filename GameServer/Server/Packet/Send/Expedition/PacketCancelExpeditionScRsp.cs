using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketCancelExpeditionScRsp : BasePacket
{
    public PacketCancelExpeditionScRsp(uint expeditionId) : base(CmdIds.CancelExpeditionScRsp)
    {
        var proto = new CancelExpeditionScRsp
        {
            Retcode = 0,
            ExpeditionId = expeditionId,
            // 中途取消通常没有奖励，初始化为空对象
            Reward = new ItemList(),
            ExtraReward = new ItemList(),
            // 结束时间设为当前服务器时间
            FinishTime = Extensions.GetUnixSec()
        };

        SetData(proto);
    }
}
