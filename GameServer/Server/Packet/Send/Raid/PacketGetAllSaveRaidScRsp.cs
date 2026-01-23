using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Raid;

public class PacketGetAllSaveRaidScRsp : BasePacket
{
   public PacketGetAllSaveRaidScRsp(PlayerInstance player) : base(CmdIds.GetAllSaveRaidScRsp)
{
    var proto = new GetAllSaveRaidScRsp();

    // 遍历玩家所有已产生的副本记录
    foreach (var dict in player.RaidManager!.RaidData.RaidRecordDatas.Values)
    {
        foreach (var record in dict.Values)
        {
            // --- 核心解锁逻辑：只卡等级和任务 ---

            // 1. 均衡等级过滤：玩家等级不够，这个难度的副本就不该出现在手册里
            if (record.WorldLevel >   player.WorldLevel)
                continue;

            // 2. 主线任务过滤：前置剧情没做完，副本就不该出现在手册里
            var raidConfig = ExcelConfig.RaidConfig.Get((uint)record.RaidId);
            if (raidConfig != null && raidConfig.UnlockMainMissionId > 0)
            {
                // 如果解锁副本的主线任务还没完成，直接跳过
                if (!player.MissionManager!.IsMissionFinished(raidConfig.UnlockMainMissionId))
                    continue;
            }

            // --- 重点：不再判断 record.Status ---
            // 只要通过了等级和任务校验，就发给客户端。
            // 这样手册里就会显示该副本，且状态会根据你 record 里的真实状态（Doing/Finish）来显示。
            
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
