using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubManager(PlayerInstance player) : BasePlayerManager(player)
{
    private static readonly Logger _log = new("BoxingClub");
    
    // 局内实例
    public BoxingClubInstance? ChallengeInstance { get; set; }

    /// <summary>
    /// 匹配对手：如果没有实例则创建，如果有了则推进匹配数据
    /// </summary>
    public FCIHIJLOMGA ProcessMatchRequest(MatchBoxingClubOpponentCsReq req)
    {
        // 1. 初始化实例
        if (ChallengeInstance == null)
        {
            ChallengeInstance = new BoxingClubInstance(Player, req.ChallengeId, req.AvatarList.ToList());
        }

        var instance = ChallengeInstance;
        uint targetEventId = 0;
        uint randomIndex = 1;

        // 2. 随机逻辑
        if (GameData.BoxingClubChallengeData.TryGetValue((int)req.ChallengeId, out var config))
        {
            if (config.StageGroupList != null && instance.CurrentRoundIndex < config.StageGroupList.Count)
            {
                var groupId = config.StageGroupList[instance.CurrentRoundIndex];
                if (GameData.BoxingClubStageGroupData.TryGetValue(groupId, out var groupConfig))
                {
                    var displayList = groupConfig.DisplayEventIDList;
                    if (displayList != null && displayList.Count > 0)
                    {
                        randomIndex = (uint)new Random().Next(1, displayList.Count + 1);
                        targetEventId = (uint)displayList[(int)randomIndex - 1];
                    }
                }
            }
        }

        // 3. 存入实例
        instance.CurrentMatchEventId = targetEventId;
        instance.CurrentOpponentIndex = randomIndex;

        return ConstructSnapshot(instance);
    }

    public FCIHIJLOMGA? ProcessChooseResonance(uint challengeId, uint resonanceId)
    {
        if (ChallengeInstance == null || ChallengeInstance.ChallengeId != challengeId) return null;

        // 增加 Buff 并推进轮次
        if (resonanceId != 0) ChallengeInstance.SelectedBuffs.Add(resonanceId);
        ChallengeInstance.CurrentRoundIndex++;

        // 检查是否通关
        if (GameData.BoxingClubChallengeData.TryGetValue((int)challengeId, out var config))
        {
            if (ChallengeInstance.CurrentRoundIndex >= (config.StageGroupList?.Count ?? 0))
            {
                var finalSnapshot = ConstructSnapshot(ChallengeInstance);
                finalSnapshot.APLKNJEGBKF = true; // 打完了，显示大勾
                
                // 通关后销毁实例并切回阵容
                _ = ChallengeInstance.OnBattleEnd(new PVEBattleResultCsReq { EndStatus = BattleEndStatus.BattleEndQuit });
                return finalSnapshot;
            }
        }

        return ConstructSnapshot(ChallengeInstance);
    }

    /// <summary>
    /// 统一构造 FCIHIJLOMGA 快照的方法
    /// </summary>
    public FCIHIJLOMGA ConstructSnapshot(BoxingClubInstance inst)
    {
        var snapshot = new FCIHIJLOMGA
        {
            ChallengeId = inst.ChallengeId,
            HJMGLEMJHKG = inst.CurrentOpponentIndex, // 转盘位置
            LLFOFPNDAFG = 1,                         // 状态：进行中
            HNPEAPPMGAA = (uint)(inst.CurrentRoundIndex + 1), // UI进度 1/4
            APLKNJEGBKF = false
        };

        if (inst.CurrentMatchEventId != 0)
            snapshot.HLIBIJFHHPG.Add(inst.CurrentMatchEventId);

        snapshot.AvatarList.AddRange(inst.SelectedAvatars);
        
        // 补全选人池 (暴力修复试用角色头像不显示)
        if (GameData.BoxingClubChallengeData.TryGetValue((int)inst.ChallengeId, out var config))
        {
            config.SpecialAvatarIDList?.ForEach(id => snapshot.MDLACHDKMPH.Add(new IJKJJDHLKLB { 
                AvatarId = (uint)id, 
                AvatarType = AvatarType.AvatarLimitType 
            }));
        }

        return snapshot;
    }
}
