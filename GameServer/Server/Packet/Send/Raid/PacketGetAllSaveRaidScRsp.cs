using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Enums.Mission;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Raid;

public class PacketGetAllSaveRaidScRsp : BasePacket
{
    public PacketGetAllSaveRaidScRsp(PlayerInstance player) : base(CmdIds.GetAllSaveRaidScRsp)
    {
        var proto = new GetAllSaveRaidScRsp();

        // 检查副本记录是否存在
        if (player.RaidManager?.RaidData?.RaidRecordDatas == null)
        {
            SetData(proto);
            return;
        }

        foreach (var dict in player.RaidManager.RaidData.RaidRecordDatas.Values)
        {
            foreach (var record in dict.Values)
            {
                // 1. 均衡等级过滤：从 PlayerInstance.Data 访问 WorldLevel
                if (record.WorldLevel > player.Data.WorldLevel)
                    continue;

                // 2. 主线任务解锁过滤
                if (GameData.RaidConfigData.TryGetValue(record.RaidId, out var raidConfig))
                {
                    // 检查副本配置中的解锁任务 ID
                    if (raidConfig.UnlockMissionId > 0)
                    {
                        // 使用 MissionManager 提供的状态查询方法
                        var status = player.MissionManager!.GetMainMissionStatus(raidConfig.UnlockMissionId);
                        
                        // 只有当任务状态为已完成（Finish）时，才同步该副本数据
                        if (status != MissionPhaseEnum.Finish)
                            continue;
                    }
                }

                // 符合条件则加入列表点亮生存手册
                // 这里不再过滤 RaidStatus，确保“解锁即显示”
                proto.RaidDataList.Add(new RaidData
                {
                    RaidId = (uint)record.RaidId,
                    WorldLevel = (uint)record.WorldLevel
                });
            }
        }

        SetData(proto);
    }
}
