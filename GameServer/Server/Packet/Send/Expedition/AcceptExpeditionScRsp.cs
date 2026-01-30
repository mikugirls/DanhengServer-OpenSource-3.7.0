using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Database.Expedition;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketAcceptExpeditionScRsp : BasePacket
{
    // 构造函数：传入刚才存入数据库的新派遣实例
    public PacketAcceptExpeditionScRsp(ExpeditionInfoInstance instance) : base(CmdIds.AcceptExpeditionScRsp)
    {
        var proto = new AcceptExpeditionScRsp
        {
            Retcode = 0,
            AcceptExpedition = new ExpeditionInfo
            {
                Id = instance.Id,
                TotalDuration = instance.TotalDuration,
                StartExpeditionTime = instance.StartExpeditionTime
            }
        };
        
        // 把角色列表也塞进去
        proto.AcceptExpedition.AvatarIdList.AddRange(instance.AvatarIdList);

        SetData(proto);
    }
}
