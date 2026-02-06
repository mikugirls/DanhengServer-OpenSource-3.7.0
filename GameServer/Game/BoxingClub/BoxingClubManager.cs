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
	 * Tag 13 (CPGOIPICPJF)  : 历史最快回合数 (MinRounds)。
	 * Tag 9  (NAALCBMBPGC)  : 当前挑战实时累计回合数 (TotalUsedTurns)决定评价。
     */
  public List<FCIHIJLOMGA> GetChallengeList()
{
    var challengeInfos = new List<FCIHIJLOMGA>();
    var dbChallenges = Player.BoxingClubData?.Challenges ?? new Dictionary<int, Database.BoxingClub.BoxingClubInfo>();

    foreach (var config in GameData.BoxingClubChallengeData.Values)
    {
        uint cid = (uint)config.ChallengeID;
        FCIHIJLOMGA info;

        // 情况 A：玩家正在挑战中，由 ConstructSnapshot 处理实时 Tag
        if (ChallengeInstance != null && ChallengeInstance.ChallengeId == cid)
        {
            info = ConstructSnapshot(ChallengeInstance);
        }
        else
        {
            // 情况 B：关卡静止态，仅同步历史数据
            dbChallenges.TryGetValue((int)cid, out var dbInfo);

            info = new FCIHIJLOMGA
            {
                ChallengeId = cid,
                LLFOFPNDAFG = 1, // 赛季 ID
                APLKNJEGBKF = dbInfo?.IsFinished ?? false, // 是否完结
                CPGOIPICPJF = (uint)(dbInfo?.MinRounds ?? 0), // 历史战绩
                // 【核心修正】不给 HNPEAPPMGAA (Tag 14), HJMGLEMJHKG (Tag 4), NAALCBMBPGC (Tag 9) 赋值
                // 此时 Protobuf 不会序列化这些字段，客户端处于“待机”状态。
            };

            // 注入选人记忆列表 (Tag 3)
            if (dbInfo?.Lineup != null && dbInfo.Lineup.Count > 0)
            {
                info.AvatarList.AddRange(dbInfo.Lineup.Select(x => (uint)x.BaseAvatarId));
            }
            else if (config.SpecialAvatarIDList != null)
            {
                foreach (var trialId in config.SpecialAvatarIDList)
                {
                    Player.AvatarManager?.GetTrialAvatar((int)trialId);
                    info.AvatarList.Add((uint)trialId);
                }
            }
        }
        challengeInfos.Add(info);
    }
    return challengeInfos;
}

 public FCIHIJLOMGA ProcessMatchRequest(MatchBoxingClubOpponentCsReq req)
{
    // 【核心修正】直接解析请求包里的 ChallengeId
    uint reqId = req.ChallengeId; 

    // 1. 全局同步点：如果实例不存在，或者请求的 ID 与当前实例记录的 ID 不一致
    // 必须重新创建实例，这样 Manager 下的所有其他方法才能拿到正确的 ChallengeInstance
    if (ChallengeInstance == null || ChallengeInstance.ChallengeId != reqId)
    {
        _log.Info($"[Match] 检测到 ID 不匹配或新挑战，全局同步 ChallengeId 为: {reqId}");
        
        var safeAvatarList = new List<uint>();
        if (req.MDLACHDKMPH is { Count: > 0 })
        {
            safeAvatarList.AddRange(req.MDLACHDKMPH.Select(a => a.AvatarId));
        }
        else
        {
            // 数据库补全逻辑...
            if (Player.BoxingClubData?.Challenges.TryGetValue((int)reqId, out var dbInfo) == true)
            {
                if (dbInfo?.Lineup != null) 
                    safeAvatarList.AddRange(dbInfo.Lineup.Select(a => (uint)a.BaseAvatarId));
            }
        }

        // --- 这一步最关键：更新了成员变量，后续所有方法调用的 ID 都会变 ---
        ChallengeInstance = new BoxingClubInstance(Player, reqId, safeAvatarList);
    }
    else 
    {
        // 如果 ID 没变，同步阵容
        if (req.MDLACHDKMPH is { Count: > 0 })
        {
            ChallengeInstance.SelectedAvatars.Clear();
            ChallengeInstance.SelectedAvatars.AddRange(req.MDLACHDKMPH.Select(a => a.AvatarId));
        }
    }

    // 2. 匹配逻辑：直接基于已经同步完成的 ChallengeInstance
    uint currentGroupId = 0;
    uint targetEventId = 0;

    if (GameData.BoxingClubChallengeData.TryGetValue((int)ChallengeInstance.ChallengeId, out var config))
    {
        // 使用同步后的 ID 查表
        int roundIdx = ChallengeInstance.CurrentRoundIndex; 
        if (config.StageGroupList != null && roundIdx < config.StageGroupList.Count)
        {
            currentGroupId = (uint)config.StageGroupList[roundIdx]; 
            if (GameData.BoxingClubStageGroupData.TryGetValue((int)currentGroupId, out var groupConfig))
            {
                targetEventId = (uint)(groupConfig.DisplayEventIDList?.FirstOrDefault() ?? 0);
            }
        }
    }

    // 3. 将计算结果回填至 Instance
    // 这样稍后 EnterBoxingClubStage 调用时，拿到的才是这次匹配出来的怪
    ChallengeInstance.CurrentStageGroupId = currentGroupId;
    ChallengeInstance.CurrentMatchEventId = targetEventId;

    return ConstructSnapshot(ChallengeInstance);
}

public FCIHIJLOMGA ConstructSnapshot(BoxingClubInstance inst)
{
    // [数据库整合]：获取持久化的历史数据
    Database.BoxingClub.BoxingClubInfo? dbInfo = null;
    Player.BoxingClubData?.Challenges.TryGetValue((int)inst.ChallengeId, out dbInfo);
	// 【核心修正逻辑】
    uint resonanceIdToSync = 1; // 默认值（Season 1）
    if (inst.ChallengeId >= 6)
    {
        // 如果是 Season 2，LLFOFPNDAFG 必须传当前生效的共鸣 ID
        // 优先取最近一次选中的 Buff
        resonanceIdToSync = inst.SelectedBuffs.LastOrDefault();
        
        // 如果还没有选过 Buff（初始状态），可以给 0 或根据配置给个默认值
        if (resonanceIdToSync == 0) resonanceIdToSync = 0; 
    }
    var snapshot = new FCIHIJLOMGA 
    {
        ChallengeId = inst.ChallengeId,
        HJMGLEMJHKG = inst.CurrentStageGroupId, // 组怪物ID
        LLFOFPNDAFG = resonanceIdToSync,        // 【同步点】Season 1 传 1, Season 2 传 BuffID                     // 赛季ID
        NAALCBMBPGC = inst.TotalUsedTurns,      // [Tag 9] 当前挑战实时累计回合数
        HNPEAPPMGAA = (uint)(inst.CurrentRoundIndex), // [Tag 14] 进度 (0-3)
        
        // [核心修正] 接入持久化字段
        APLKNJEGBKF = dbInfo?.IsFinished ?? false,      // [Tag 10] 通关勾勾
        CPGOIPICPJF = (uint)(dbInfo?.MinRounds ?? 0)    // [Tag 13] 历史最快回合
    };

    // 1. 同步选人记忆列表 (Tag 3)
    if (inst.SelectedAvatars.Count > 0)
    {
        snapshot.AvatarList.Clear();
        snapshot.AvatarList.AddRange(inst.SelectedAvatars);

        // 2. 同步详细的出战阵容详情 (Tag 6)
        snapshot.MDLACHDKMPH.Clear();
        foreach (var avatarId in inst.SelectedAvatars)
        {
            snapshot.MDLACHDKMPH.Add(new IJKJJDHLKLB 
            { 
                AvatarId = avatarId, 
                AvatarType = AvatarType.AvatarFormalType 
            });
        }
        
        if (EnableLog) _log.Info($"[Sync] 快照队伍注入: {string.Join(",", inst.SelectedAvatars)}");
    }

    // 3. 注入匹配到的对手 ID 池 (Tag 1)
    // 第一次进入或匹配时注入当前 StageGroup 的完整 Event 列表，解决转盘报错
    if (inst.CurrentStageGroupId != 0 && GameData.BoxingClubStageGroupData.TryGetValue((int)inst.CurrentStageGroupId, out var groupConfig))
    {
        snapshot.HLIBIJFHHPG.Clear();
        if (groupConfig.DisplayEventIDList != null)
        {
            snapshot.HLIBIJFHHPG.AddRange(groupConfig.DisplayEventIDList.Select(x => (uint)x));
        }
        _log.Info($"[Sync] 对手池(Tag 1)已注入 {snapshot.HLIBIJFHHPG.Count} 个对手。");
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
        
        // 增加数据库读取以保持 UI 状态
        // 修复 CS0165: 显式初始化 dbInfo
        Database.BoxingClub.BoxingClubInfo? dbInfo = null;
        Player.BoxingClubData?.Challenges.TryGetValue((int)challengeId, out dbInfo);
        
        return new FCIHIJLOMGA 
        { 
            ChallengeId = challengeId, 
            LLFOFPNDAFG = 1,
            APLKNJEGBKF = dbInfo?.IsFinished ?? false,
            CPGOIPICPJF = (uint)(dbInfo?.MinRounds ?? 0)
        };
    }
/// <summary>
/// 1. 赛季 2 初始共鸣选择 (4281)
/// </summary>
public FCIHIJLOMGA? ProcessChooseResonance(uint challengeId, uint resonanceId)
{
    _log.Info($"[Boxing] 初始共鸣选择: ChallengeId {challengeId}, ResonanceId {resonanceId}");

    // 初始化实例
    if (ChallengeInstance == null || ChallengeInstance.ChallengeId != challengeId)
    {
        ChallengeInstance = new BoxingClubInstance(Player, challengeId, new List<uint>());
    }

    // 记录 Buff
    if (resonanceId != 0 && !ChallengeInstance.SelectedBuffs.Contains(resonanceId))
    {
        ChallengeInstance.SelectedBuffs.Add(resonanceId);
    }

    // 查找映射组
    if (GameData.BoxingClubChallengeData.TryGetValue((int)challengeId, out var config))
    {
        if (config.StageBuffAndGroupMap != null && 
            config.StageBuffAndGroupMap.TryGetValue(resonanceId.ToString(), out var gId))
        {
            ChallengeInstance.CurrentStageGroupId = (uint)gId;
        }
    }

    // 统一执行匹配逻辑 (根据当前 RoundIndex 分配怪)
    UpdateMatchEvent(ChallengeInstance);

    return ConstructSnapshot(ChallengeInstance);
}

/// <summary>
/// 2. 赛季 2 战斗间隙 Buff 选择 (4292) - 解决“怪不变”的关键
/// </summary>
public FCIHIJLOMGA? ProcessOptionalBuff(uint challengeId, uint optionalBuffId)
{
    _log.Info($"[Boxing] 战斗间隙选 Buff: ChallengeId {challengeId}, BuffId {optionalBuffId}");

    if (ChallengeInstance == null) return null;

    // 记录新增 Buff
    if (optionalBuffId != 0 && !ChallengeInstance.SelectedBuffs.Contains(optionalBuffId))
    {
        ChallengeInstance.SelectedBuffs.Add(optionalBuffId);
    }

    // 赛季 2 核心：中间选的 Buff 会决定下一轮进哪个 StageGroup
    if (GameData.BoxingClubChallengeData.TryGetValue((int)challengeId, out var config))
    {
        if (config.StageBuffAndGroupMap != null && 
            config.StageBuffAndGroupMap.TryGetValue(optionalBuffId.ToString(), out var gId))
        {
            ChallengeInstance.CurrentStageGroupId = (uint)gId;
            _log.Info($"[Boxing] 路径分支切换 -> 新 GroupId: {gId}");
        }
    }

    // 重新匹配怪物（此时 RoundIndex 应该已经被胜利逻辑累加了）
    UpdateMatchEvent(ChallengeInstance);

    return ConstructSnapshot(ChallengeInstance);
}

/// <summary>
/// 3. 阵容同步 (4257)
/// </summary>
public FCIHIJLOMGA? ProcessResonanceLineup(uint challengeId, IList<GNEIBBPOAAB> avatarList)
{
    var selectedIds = avatarList.Select(a => a.AvatarId).ToList();
    _log.Info($"[Boxing] 选人同步: Challenge {challengeId}, 阵容: {string.Join(",", selectedIds)}");

    if (ChallengeInstance == null || ChallengeInstance.ChallengeId != challengeId)
    {
        ChallengeInstance = new BoxingClubInstance(Player, challengeId, selectedIds);
        // 如果是直接跳到这步的，补一下匹配逻辑
        UpdateMatchEvent(ChallengeInstance);
    }
    else
    {
        ChallengeInstance.SelectedAvatars.Clear();
        ChallengeInstance.SelectedAvatars.AddRange(selectedIds);
    }

    return ConstructSnapshot(ChallengeInstance);
}

/// <summary>
/// 通用私有方法：基于当前组和轮次索引精准匹配怪物
/// </summary>
private void UpdateMatchEvent(BoxingClubInstance inst)
{
    if (GameData.BoxingClubStageGroupData.TryGetValue((int)inst.CurrentStageGroupId, out var groupConfig))
    {
        var eventList = groupConfig.EventIDList;
        if (eventList != null && eventList.Count > 0)
        {
            // 根据当前轮次索引（0=第一场, 1=第二场...）取怪
            int index = (int)inst.CurrentRoundIndex;
            
            // 安全检查：如果配置里只有1个怪但打到了第2轮，则取最后一个
            if (index >= eventList.Count) index = eventList.Count - 1;

            inst.CurrentMatchEventId = (uint)eventList[index];
            
            _log.Info($"[Boxing] 匹配成功 -> 第 {inst.CurrentRoundIndex + 1} 场, EventId: {inst.CurrentMatchEventId}");
        }
    }
}
 
}
