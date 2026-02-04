using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.Enums.Avatar;

namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubManager(PlayerInstance player) : BasePlayerManager(player)
{
    private static readonly Logger _log = new("BoxingClub");
    public static bool EnableLog = true; 
    
    public BoxingClubInstance? ChallengeInstance { get; set; }

    /// <summary>
    /// 【核心修正】从 LineupManager 实时抓取数据。
    /// 这保证了数据源与 GetLineupAvatarDataScRsp (协议 757) 保持绝对同步。
    /// </summary>
    private List<uint> GetLineupDataFromManager()
    {
        var curLineup = Player.LineupManager?.GetCurLineup();
        // 修正：根据 LineupManager.cs 源码，字段名应为 BaseAvatars
        if (curLineup != null && curLineup.BaseAvatars != null && curLineup.BaseAvatars.Count > 0)
        {
            // 提取 BaseAvatarId，这包含了正式角色和试用角色的基础ID
            return curLineup.BaseAvatars.Select(a => (uint)a.BaseAvatarId).ToList();
        }
        return new List<uint>();
    }

    public List<FCIHIJLOMGA> GetChallengeList()
    {
        var challengeInfos = new List<FCIHIJLOMGA>();
        if (EnableLog) _log.Debug($"[Sync] 正在同步玩家 {Player.Uid} 的搏击挑战列表...");

        foreach (var config in GameData.BoxingClubChallengeData.Values)
        {
            FCIHIJLOMGA info;
            if (ChallengeInstance != null && ChallengeInstance.ChallengeId == (uint)config.ChallengeID)
            {
                info = ConstructSnapshot(ChallengeInstance);
            }
            else
            {
                info = new FCIHIJLOMGA 
                { 
                    ChallengeId = (uint)config.ChallengeID, 
                    LLFOFPNDAFG = 1, 
                    APLKNJEGBKF = false,
                    CPGOIPICPJF = 0,
                    NAALCBMBPGC = 0,
                };
            }

            // 填充试用角色池 (MDLACHDKMPH)
            if (config.SpecialAvatarIDList != null)
            {
                foreach (var trialId in config.SpecialAvatarIDList)
                {
                    info.MDLACHDKMPH.Add(new IJKJJDHLKLB {
                        AvatarId = (uint)trialId,
                        AvatarType = AvatarType.AvatarLimitType 
                    });
                }
            }

            // 【核心修正】确保列表同步时也包含队伍，防止进入战斗前阵容在 UI 消失
            if (ChallengeInstance != null && ChallengeInstance.ChallengeId == (uint)config.ChallengeID)
            {
                if (ChallengeInstance.SelectedAvatars.Count > 0)
                {
                    if (EnableLog) _log.Info($"[Sync] 快照队伍注入: {string.Join(",", ChallengeInstance.SelectedAvatars)}");
                    info.AvatarList.Clear();
                    info.AvatarList.AddRange(ChallengeInstance.SelectedAvatars);
                }
            }

            challengeInfos.Add(info);
        }
        return challengeInfos;
    }

    public FCIHIJLOMGA ProcessMatchRequest(MatchBoxingClubOpponentCsReq req)
    {
        var safeAvatarList = new List<uint>();
        
        // 1. 尝试从匹配请求的混淆字段读取客户端上传的阵容
        if (req.MDLACHDKMPH != null && req.MDLACHDKMPH.Count > 0)
        {
            safeAvatarList.AddRange(req.MDLACHDKMPH.Select(a => a.AvatarId));
        }

        // 2. 如果请求为空，则直接从 GetLineupAvatarData 对应的数据源强制同步
        if (safeAvatarList.Count == 0)
        {
            safeAvatarList = GetLineupDataFromManager();
            if (EnableLog) _log.Info($"[Match] 请求阵容为空，已从 LineupManager 强制同步: {string.Join(",", safeAvatarList)}");
        }

        if (EnableLog) _log.Info($"[Match] 确认出战人数: {safeAvatarList.Count}，准备匹配对手...");

        if (ChallengeInstance == null)
        {
            ChallengeInstance = new BoxingClubInstance(Player, req.ChallengeId, safeAvatarList);
        }
        else if (safeAvatarList.Count > 0)
        {
            ChallengeInstance.SelectedAvatars.Clear();
            ChallengeInstance.SelectedAvatars.AddRange(safeAvatarList);
        }

        uint targetEventId = 0;
        uint randomIndex = 1;
        if (GameData.BoxingClubChallengeData.TryGetValue((int)req.ChallengeId, out var config))
        {
            if (config.StageGroupList != null && ChallengeInstance.CurrentRoundIndex < config.StageGroupList.Count)
            {
                var groupId = config.StageGroupList[ChallengeInstance.CurrentRoundIndex];
                if (GameData.BoxingClubStageGroupData.TryGetValue(groupId, out var groupConfig))
                {
                    int displayCount = groupConfig.DisplayEventIDList?.Count ?? 0;
                    if (displayCount > 0)
                    {
                        randomIndex = (uint)new Random().Next(1, displayCount + 1);
                        targetEventId = (uint)groupConfig.DisplayEventIDList![(int)randomIndex - 1];
                    }
                }
            }
        }

        ChallengeInstance.CurrentMatchEventId = targetEventId;
        ChallengeInstance.CurrentOpponentIndex = randomIndex;

        return ConstructSnapshot(ChallengeInstance);
    }

    public FCIHIJLOMGA ConstructSnapshot(BoxingClubInstance inst)
    {
        var snapshot = new FCIHIJLOMGA 
        {
			ChallengeId = inst.ChallengeId,
            HJMGLEMJHKG = inst.CurrentOpponentIndex,
            LLFOFPNDAFG = 1,
            HNPEAPPMGAA = (uint)(inst.CurrentRoundIndex)
        };

        // 【关键】每一包下发的快照必须包含 SelectedAvatars
        if (inst.SelectedAvatars.Count > 0)
        {
            snapshot.AvatarList.Clear();
            snapshot.AvatarList.AddRange(inst.SelectedAvatars);
        }

        if (inst.CurrentMatchEventId != 0) 
            snapshot.HLIBIJFHHPG.Add(inst.CurrentMatchEventId);

        if (GameData.BoxingClubChallengeData.TryGetValue((int)inst.ChallengeId, out var config))
        {
            config.SpecialAvatarIDList?.ForEach(id => snapshot.MDLACHDKMPH.Add(new IJKJJDHLKLB { 
                AvatarId = (uint)id, 
                AvatarType = AvatarType.AvatarLimitType 
            }));
        }

        return snapshot;
    }

    public async ValueTask EnterBoxingClubStage(uint challengeId)
    {
        if (EnableLog) _log.Info($"[Enter] 玩家请求启动战斗, ID: {challengeId}");
        if (ChallengeInstance != null && ChallengeInstance.ChallengeId == challengeId)
        {
            // 最后一道防线：如果还是没阵容，强行从同步源再捞一次
            if (ChallengeInstance.SelectedAvatars.Count == 0)
            {
                var rescue = GetLineupDataFromManager();
                if (rescue.Count > 0)
                {
                    _log.Warn("[Enter] 阵容丢失，已通过 LineupManager 实时源补回。");
                    ChallengeInstance.SelectedAvatars.AddRange(rescue);
                }
                else
                {
                    _log.Error("[Enter] 拒绝进入：阵容数据完全缺失。");
                    return;
                }
            }
            await ChallengeInstance.EnterStage();
        }
    }

    public FCIHIJLOMGA ProcessGiveUpChallenge(uint challengeId, bool isFullReset)
    {
        if (isFullReset) ChallengeInstance = null;
        return new FCIHIJLOMGA { ChallengeId = challengeId, LLFOFPNDAFG = 1 };
    }

    public FCIHIJLOMGA? ProcessChooseResonance(uint challengeId, uint resonanceId)
    {
        if (ChallengeInstance == null) return null;
        if (resonanceId != 0) ChallengeInstance.SelectedBuffs.Add(resonanceId);
        ChallengeInstance.CurrentRoundIndex++;
        ChallengeInstance.CurrentMatchEventId = 0;
        ChallengeInstance.CurrentOpponentIndex = 0;
        return ConstructSnapshot(ChallengeInstance);
    }
}