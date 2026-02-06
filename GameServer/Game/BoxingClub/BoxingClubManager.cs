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
        if (EnableLog) _log.Debug($"[Sync] 正在加载玩家 {Player.Uid} 的数据库持久化搏击记录...");

        // 获取数据库对象中的挑战字典 (增加安全空检查)
        var dbChallenges = Player.BoxingClubData?.Challenges ?? new Dictionary<int, Database.BoxingClub.BoxingClubInfo>();

        foreach (var config in GameData.BoxingClubChallengeData.Values)
        {
            uint cid = (uint)config.ChallengeID;
            FCIHIJLOMGA info;

            // 优先检查：如果玩家正在打这一关，直接构造内存快照
            if (ChallengeInstance != null && ChallengeInstance.ChallengeId == cid)
            {
                info = ConstructSnapshot(ChallengeInstance);
            }
            else
            {
                // 否则从数据库加载持久化数据
                dbChallenges.TryGetValue((int)cid, out var dbInfo);

                info = new FCIHIJLOMGA
                {
                    ChallengeId = cid,
                    LLFOFPNDAFG = 1,                 // 赛季 ID
                    APLKNJEGBKF = dbInfo?.IsFinished ?? false,      // Tag 10: 完结勾勾 (数据库存)
                    CPGOIPICPJF = (uint)(dbInfo?.MinRounds ?? 0),   // Tag 13: 历史最快回合 (数据库存)
                    NAALCBMBPGC = 0,                                // Tag 9: 实时回合 (非挑战中设为0)
                    HNPEAPPMGAA = 0                                 // Tag 14: 进度 (非挑战中设为0)
                };

                // 注入记忆阵容 (Tag 3)
                if (dbInfo?.Lineup != null && dbInfo.Lineup.Count > 0)
                {
                    // 将数据库里的 LineupAvatarInfo 列表转为 UI 显示用的 uint ID 列表
                    info.AvatarList.AddRange(dbInfo.Lineup.Select(x => (uint)x.BaseAvatarId));
                }
                else
                {
                    // 如果数据库里没存阵容，则注入配置表里的默认试用角色
                    if (config.SpecialAvatarIDList != null)
                    {
                        foreach (var trialId in config.SpecialAvatarIDList)
                        {
                            Player.AvatarManager?.GetTrialAvatar((int)trialId);
                            info.AvatarList.Add((uint)trialId);
                        }
                    }
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
        
        // 优先使用请求中的阵容
        if (req.MDLACHDKMPH != null && req.MDLACHDKMPH.Count > 0)
        {
            safeAvatarList.AddRange(req.MDLACHDKMPH.Select(a => a.AvatarId));
        }
        else
        {
            // [数据库整合]：如果请求没传阵容，尝试从数据库获取该关卡的记忆阵容 (Tag 3)
            // 修复 CS0165: 显式初始化 dbInfo
            Database.BoxingClub.BoxingClubInfo? dbInfo = null;
			var dbChallenges = Player.BoxingClubData?.Challenges;
            if (dbChallenges != null && dbChallenges.TryGetValue((int)req.ChallengeId, out dbInfo))
			{
				// 如果找到了数据库记录，执行相关逻辑
				_log.Info($"[Match] 从数据库恢复记忆阵容: ChallengeId {req.ChallengeId}");
				if (dbInfo?.Lineup != null) 
				{
					safeAvatarList.AddRange(dbInfo.Lineup.Select(a => (uint)a.BaseAvatarId));
				}
			}
        }
        
        ChallengeInstance = new BoxingClubInstance(Player, req.ChallengeId, safeAvatarList);
    }
    else 
    {
        // 2. 后续轮次更新阵容
        if (req.MDLACHDKMPH != null && req.MDLACHDKMPH.Count > 0)
        {
            ChallengeInstance.SelectedAvatars.Clear();
            ChallengeInstance.SelectedAvatars.AddRange(req.MDLACHDKMPH.Select(a => a.AvatarId));
        }
    }

    // 3. 执行核心匹配计算
    uint currentGroupId = 0;
    uint targetEventId = 0;

    if (GameData.BoxingClubChallengeData.TryGetValue((int)ChallengeInstance.ChallengeId, out var config))
    {
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

    ChallengeInstance.CurrentStageGroupId = currentGroupId;
    ChallengeInstance.CurrentMatchEventId = targetEventId;

    _log.Info($"[Match-Check] 轮次: {ChallengeInstance.CurrentRoundIndex}, 分组: {currentGroupId}, 对手: {targetEventId}");
    
    return ConstructSnapshot(ChallengeInstance);
}

public FCIHIJLOMGA ConstructSnapshot(BoxingClubInstance inst)
{
    // [数据库整合]：获取持久化的历史数据
    Database.BoxingClub.BoxingClubInfo? dbInfo = null;
    Player.BoxingClubData?.Challenges.TryGetValue((int)inst.ChallengeId, out dbInfo);

    var snapshot = new FCIHIJLOMGA 
    {
        ChallengeId = inst.ChallengeId,
        HJMGLEMJHKG = inst.CurrentStageGroupId, // 组怪物ID
        LLFOFPNDAFG = 1,                        // 赛季ID
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
