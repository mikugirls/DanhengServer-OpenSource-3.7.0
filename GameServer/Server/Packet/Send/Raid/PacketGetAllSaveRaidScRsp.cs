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

        // 检查 RaidManager 是否初始化
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
                // 根据 RaidConfigExcel.GetId()，Key 为 RaidID * 100 + HardLevel
                int configId = record.RaidId * 100 + record.WorldLevel;
                
                if (GameData.RaidConfigData.TryGetValue(configId, out var raidConfig))
                {
                    bool isLocked = false;
                    // 检查列表中的所有前置主线任务是否都已完成
                    foreach (var missionId in raidConfig.MainMissionIDList)
                    {
                        // 使用 MissionManager 的 GetMainMissionStatus 判定
                        var status = player.MissionManager!.GetMainMissionStatus(missionId);
                        if (status != MissionPhaseEnum.Finish)
                        {
                            isLocked = true;
                            break;
                        }
                    }

                    if (isLocked) continue;
                }

                // 符合条件则同步给客户端，不管状态，只要解锁了就是要点亮
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
