using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Database.Expedition; // 确保引用了实例类所在的命名空间

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

public class PacketExpeditionDataChangeScNotify : BasePacket
{
    /// <summary>
    /// 构造全量同步通知
    /// </summary>
    /// <param name="teamCount">解锁的派遣槽位上限</param>
    /// <param name="expeditions">当前进行中的派遣列表</param>
    /// <param name="unlockedIds">已解锁的派遣点 ID 列表</param>
    public PacketExpeditionDataChangeScNotify(uint teamCount, List<ExpeditionInfoInstance> expeditions, List<uint> unlockedIds) 
        : base(CmdIds.ExpeditionDataChangeScNotify)
    {
        var proto = new ExpeditionDataChangeScNotify
        {
            // 对应 Tag 13: 当前可用槽位总数
            TeamCount = teamCount,
            
            // 对应 Tag 10: 正在运行的派遣实例
            ExpedtionList = { expeditions.Select(x => x.ToProto()) },
            
            // 对应 Tag 3: 地图上已解锁的点位列表
            UnlockedExpeditionIdList = { unlockedIds }
        };

        // 处理混淆字段 (Tag 12 和 Tag 6)，若无特殊逻辑可留空或初始化
        // proto.JFJPADLALMD.AddRange(new uint[] { }); 
        // proto.FNALLOLDGLM.AddRange(new uint[] { });

        SetData(proto);
    }
}
