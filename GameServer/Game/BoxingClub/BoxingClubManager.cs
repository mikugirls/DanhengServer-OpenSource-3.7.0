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
	/// <summary>
    /// 获取挑战列表/快照协议数据重构 (基于 3.7.0 暴力测试最终修正版)
    /// 混淆类 FCIHIJLOMGA 字段业务映射说明:
    /// </summary>
   /// <summary>
    /// 获取挑战列表/快照协议数据重构 (基于 3.7.0 磐岩镇超级联赛最终修正版)
    /// 混淆类 FCIHIJLOMGA 字段业务映射及逻辑说明:
    /// </summary>
    /* * * * Tag 1  (HLIBIJFHHPG) : [核心] 转盘怪物内容池 (repeated uint32)。
     * 必须填充当前 StageGroup 的 DisplayEventIDList 全集。
     * 注意：若只填单个 ID，客户端会因数组长度不足导致转盘动画跳过或 UI 报错。
     * * * * Tag 2  (ChallengeId) : 挑战关卡 ID (1-5)。
     * 对应 BoxingClubChallenge.json 配置。
     * * * * Tag 3  (AvatarList)  : 选人记忆列表 (repeated uint32)。
     * 记录玩家在该关卡上次使用的阵容，实现 UI 界面记忆功能。
     * * * * Tag 4  (HJMGLEMJHKG) : [关键] 当前关卡组 ID (StageGroupID)。
     * 对应配置表的 StageGroupID (如 10, 11...)。
     * 核心逻辑：第一轮开始时确定的 ID，后续轮次【必须固定】。
     * 组ID若发生变化，客户端会重新加载场景背景，导致 UI 转盘动画中断或闪烁。
     * * * * Tag 6  (MDLACHDKMPH) : [修正] 当前选定的出战阵容 (repeated uint32)。
     * 控制选人界面中已上阵角色的头像显示，确保战斗前后阵容一致。
     * * * * Tag 8  (LLFOFPNDAFG) : [修正] 赛季 ID (Season ID)。
     * 对应活动赛季标识。固定下发 1 或配置表中的 SeasonID，代表当前活动所属赛季。
     * * * * Tag 10 (APLKNJEGBKF) : 关卡完结标志 (bool)。
     * 控制活动主界面关卡入口处是否渲染“已完成”的大勾图标。
     * 当第四轮战斗结束且领奖后，需设为 true 以持久化完成状态。
     * * * * Tag 14 (HNPEAPPMGAA) : [进度] 当前关卡进度计数 (uint)。
     * UI 翻页的核心开关。后端从 0 开始计数（界面显示 1/4）。
     * 打完第 1 轮后发 1（显示 2/4），以此类推。
     * 只有此值发生递增位移，客户端才会启动转盘动画并滚动至下一位对手。
	 *  Tag 13 (CPGOIPICPJF)  : 历史最快回合数 (MinRounds)。
	 *  Tag 9  (NAALCBMBPGC)  : 当前挑战实时累计回合数 (TotalUsedTurns)决定评价。
     */
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
			// --- 【修改处：直接注入试用 ID】 ---
        	// 不再判断当前是什么 ID，直接把配置表里这一关的试用角色塞进 avatar_list
        if (config.SpecialAvatarIDList != null)
        {
            foreach (var trialId in config.SpecialAvatarIDList)
            {
                // 1. 确保服务器为这些试用 ID 生成了内存数据（供后续详情请求使用）
                // 显式转换为 int 以匹配 GetTrialAvatar 的参数要求
				Player.AvatarManager!.GetTrialAvatar((int)trialId);
                // 2. 直接 Add 到当前关卡的 avatar_list 字段中
                if (!info.AvatarList.Contains((uint)trialId))
                {
                    info.AvatarList.Add((uint)trialId);
                }
            }
        }
            // ------------------------------------------
          

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
    // 1. 如果是第一轮初始化
    if (ChallengeInstance == null)
    {
        var safeAvatarList = new List<uint>();
        if (req.MDLACHDKMPH != null) safeAvatarList.AddRange(req.MDLACHDKMPH.Select(a => a.AvatarId));
        ChallengeInstance = new BoxingClubInstance(Player, req.ChallengeId, safeAvatarList);
    }
    else 
    {
        // 2. 如果是后续轮次（OnBattleEnd触发），确保不丢失已选阵容
        // 只有当请求带了新阵容时才更新，否则保持原样
        if (req.MDLACHDKMPH != null && req.MDLACHDKMPH.Count > 0)
        {
            ChallengeInstance.SelectedAvatars.Clear();
            ChallengeInstance.SelectedAvatars.AddRange(req.MDLACHDKMPH.Select(a => a.AvatarId));
        }
    }

    // 3. 执行核心匹配计算 (这一步必须确保使用的是最新的 ChallengeInstance.CurrentRoundIndex)
    uint currentGroupId = 0;
    uint targetEventId = 0;

    if (GameData.BoxingClubChallengeData.TryGetValue((int)ChallengeInstance.ChallengeId, out var config))
    {
        int roundIdx = ChallengeInstance.CurrentRoundIndex; // 这里已经是 1 了
        if (config.StageGroupList != null && roundIdx < config.StageGroupList.Count)
        {
            currentGroupId = (uint)config.StageGroupList[roundIdx]; // 积分赛1这里应该是 11
            
            if (GameData.BoxingClubStageGroupData.TryGetValue((int)currentGroupId, out var groupConfig))
            {
                // 简化逻辑：直接取该组配置的 EventID
                targetEventId = (uint)(groupConfig.DisplayEventIDList?.FirstOrDefault() ?? 0);
            }
        }
    }

    // 4. 强制写入实例
    ChallengeInstance.CurrentStageGroupId = currentGroupId;
    ChallengeInstance.CurrentMatchEventId = targetEventId;

    _log.Info($"[Match-Check] 轮次: {ChallengeInstance.CurrentRoundIndex}, 分组: {currentGroupId}, 对手: {targetEventId}");
    
    return ConstructSnapshot(ChallengeInstance);
}

 public FCIHIJLOMGA ConstructSnapshot(BoxingClubInstance inst)
{
    var snapshot = new FCIHIJLOMGA 
    {
        ChallengeId = inst.ChallengeId,
        HJMGLEMJHKG = inst.CurrentStageGroupId, // 选择哪一个组怪物
        LLFOFPNDAFG = 1, 
		NAALCBMBPGC = 0, 	// 总轮次
        HNPEAPPMGAA = (uint)(inst.CurrentRoundIndex) // 进度 (0)
    };

    // 1. 同步简单的 ID 列表
    if (inst.SelectedAvatars.Count > 0)
    {
        snapshot.AvatarList.Clear();
        snapshot.AvatarList.AddRange(inst.SelectedAvatars);

        // 2. 【核心修正】同步详细的出战阵容详情
        // 客户端需要在这里看到你选中的那几个人，UI 才会跳转
        snapshot.MDLACHDKMPH.Clear();
        foreach (var avatarId in inst.SelectedAvatars)
        {
            snapshot.MDLACHDKMPH.Add(new IJKJJDHLKLB 
            { 
                AvatarId = avatarId, 
                // 暂时全部标记为正式角色类型，确保匹配校验通过
                AvatarType = AvatarType.AvatarFormalType 
            });
        }
        
        if (EnableLog) _log.Info($"[Sync] 快照双列表注入完成: {string.Join(",", inst.SelectedAvatars)}");
    }

    // 3. 注入匹配到的对手 ID
    if (inst.CurrentMatchEventId != 0) 
    {
        snapshot.HLIBIJFHHPG.Clear();
        snapshot.HLIBIJFHHPG.Add(inst.CurrentMatchEventId);
		_log.Info($"[Sync] 对手 ID注入完成: {string.Join(",", inst.CurrentMatchEventId)}");
    }

    return snapshot;
}

    // 修改返回类型为 FCIHIJLOMGA
    public async ValueTask<FCIHIJLOMGA?> EnterBoxingClubStage(uint challengeId)
    {
        if (EnableLog) _log.Info($"[Enter] 玩家请求启动战斗, ID: {challengeId}");
        
        if (ChallengeInstance != null && ChallengeInstance.ChallengeId == challengeId)
        {
            // 阵容补救逻辑
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
                    return null; // 或者返回一个空的快照
                }
            }

            // 执行进入战斗逻辑（内部会处理 Slot 19 写入）
            await ChallengeInstance.EnterStage();

            // --- 【核心修正】在这里返回完整的快照 ---
            // 这样 StartBoxingClubBattleScRsp 就能带上包含 AvatarList 的数据发给客户端
            return ConstructSnapshot(ChallengeInstance);
        }

        return null;
    }

    public FCIHIJLOMGA ProcessGiveUpChallenge(uint challengeId, bool isFullReset)
    {
        if (isFullReset) ChallengeInstance = null;
        return new FCIHIJLOMGA { ChallengeId = challengeId, LLFOFPNDAFG = 1 };
    }

   public FCIHIJLOMGA? ProcessChooseResonance(uint challengeId, uint resonanceId)
{
    if (ChallengeInstance == null) return null;
    
    _log.Info($"[Boxing] 选Buff开始，ID: {resonanceId}");

    // 1. 记录 Buff
    if (resonanceId != 0) ChallengeInstance.SelectedBuffs.Add(resonanceId);

    // 2. 先增加轮次进度 (重要：先加进度，ProcessMatchRequest 才会取到下一轮的 GroupID)
    ChallengeInstance.CurrentRoundIndex++;
    
    // 3. 【核心修正】模拟匹配请求，为下一轮生成 MatchEventId 和 StageGroupId
    var mockReq = new MatchBoxingClubOpponentCsReq { ChallengeId = challengeId };
    ProcessMatchRequest(mockReq); 

    _log.Info($"[Boxing] 选Buff完成。进入第 {ChallengeInstance.CurrentRoundIndex} 轮, 新对手ID: {ChallengeInstance.CurrentMatchEventId}");

    // 4. 返回包含新对手池和新进度的快照
    return ConstructSnapshot(ChallengeInstance);
}
}
