using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Database.Challenge; 

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Challenge;

public class PacketGetChallengeScRsp : BasePacket
{
    public PacketGetChallengeScRsp(PlayerInstance player) : base(CmdIds.GetChallengeScRsp)
    {
        var proto = new GetChallengeScRsp { Retcode = 0 };
        var historyMap = player.ChallengeManager?.ChallengeData.History;

        foreach (var challengeExcel in GameData.ChallengeConfigData.Values)
        {
            int currentId = challengeExcel.ID;
            int preId = challengeExcel.PreChallengeMazeID;
            int currentGroupId = challengeExcel.GroupID;

            // 获取历史记录
            ChallengeHistoryData? currentRecord = null;
            bool hasHistory = historyMap?.TryGetValue(currentId, out currentRecord) ?? false;

            // --- 核心逻辑控制 ---
            bool shouldSend = false; // 默认不发送
            bool isFullData = false; // 标记是否为带星数据

            // 1. 已通关判定：星数 >= 1 发送成绩
            if (hasHistory && currentRecord != null && currentRecord.Stars >= 1)
            {
                shouldSend = true;
                isFullData = true;
            }
            // 2. 初始入口判定：100组01、虚构/末日首关强制开启
            else if (currentId == 1 )
            {
                shouldSend = true;
            }
            // 3. 仙舟开启判定：依赖100组15关通关
            else if (currentGroupId == 900 && preId == 15)
            {
                if (historyMap != null && historyMap.TryGetValue(15, out var rec15) && rec15.Stars >= 1)
                {
                    shouldSend = true;
                }
            }
            // 4. 混沌开启判定：依赖仙舟6关(ID 26)通关
            else if (currentGroupId != 100 && currentGroupId != 900 && !challengeExcel.IsStory() && !challengeExcel.IsBoss() && preId == 26)
            {
                if (historyMap != null && historyMap.TryGetValue(26, out var rec26) && rec26.Stars >= 1)
                {
                    shouldSend = true;
                }
            }

            // --- 统一执行下发封装 ---
            // 只有 shouldSend 为 TRUE 时才处理 Add 操作
            if (shouldSend)
            {
                if (isFullData && currentRecord != null)
                {
                    proto.ChallengeList.Add(currentRecord.ToProto());
                }
                else
                {
                    proto.ChallengeList.Add(new Proto.Challenge { ChallengeId = (uint)currentId });
                }
            }
        }

        // 处理奖励领取
        var takenRewards = player.ChallengeManager?.ChallengeData?.TakenRewards;
        if (takenRewards != null)
        {
            foreach (var reward in takenRewards.Values)
                proto.ChallengeGroupList.Add(reward.ToProto());
        }

        SetData(proto);
    }
}