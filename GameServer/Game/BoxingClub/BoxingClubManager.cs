using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Battle;
using EggLink.DanhengServer.GameServer.Game.Scene;            
using EggLink.DanhengServer.Database.Avatar;                 
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Scene;
using EggLink.DanhengServer.Enums.Avatar;
using EggLink.DanhengServer.Util; // 确保引用了 Logger 所在的命名空间
using EggLink.DanhengServer.Database.Lineup; // 修复 LineupAvatarInfo 找不到
using EggLink.DanhengServer.GameServer.Game.Battle.Custom; // 修复 BattleBoxingClubOptions 找不到

namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubManager(PlayerInstance player) : BasePlayerManager(player)
{
    // --- 全局调试日志开关 ---
    public static bool EnableLog = true; 
	public List<uint> CurrentChallengeBuffs { get; set; } = new();
    // 初始化自定义 Logger 实例，模块名为 BoxingClub
    private static readonly Logger _log = new("BoxingClub");

    // 当前挑战的轮次下标 (0, 1, 2...) -> 对应 StageGroupList 的索引
    public int CurrentRoundIndex { get; set; } = 0;
    
    // 当前随机到的具体 EventID (用于进战斗加载具体的 Stage)
    public uint CurrentMatchEventId { get; set; } = 0;
    
    // 当前随机到的索引 (1-based，对应 DisplayEventIDList 的位置)
    public uint CurrentOpponentIndex { get; set; } = 0;

    // 当前正在进行的挑战 ID (如 1 代表羽量级)
    public uint CurrentChallengeId { get; set; } = 0;

    // 记忆阵容：存储玩家选中的英雄 ID 列表，用于解决“角色离队”Bug
    public List<uint> LastMatchAvatars { get; set; } = new();

    /// <summary>
    /// 获取挑战列表协议数据重构 (基于暴力测试截图修正版)
    /// 混淆类 FCIHIJLOMGA 字段业务映射说明:
    /// Tag 1  (HLIBIJFHHPG)  : 【核心】本次匹配到的 EventID 列表 (repeated uint)。必须下发全量(通常为4个)，否则转盘无法渲染或点击匹配无效。
    /// Tag 2  (ChallengeId)  : 关卡 ID (羽量级/轻量级等)。
    /// Tag 3  (AvatarList)   : 选人记忆列表 (SelectedAvatars)。解决已选角色离队及记录上次出战阵容。
    /// Tag 4  (HJMGLEMJHKG)  : 【关键】对手位置索引 (1-based)。对应配置表 DisplayIndexList，控制光圈停在转盘哪个物理坑位。
    /// Tag 6  (MDLACHDKMPH)  : 限定试用角色池 (SpecialAvatarList)。解决选人界面无官方提供角色的头像问题。
    /// Tag 8  (LLFOFPNDAFG)  : 挑战状态机 (Status)。1:未开始(显示开始挑战), 2:进行中(显示继续挑战), 3:已通关(与Tag 10配合显示大勾)。
    /// Tag 9  (NAALCBMBPGC)  : 累计轮次数显示 (TotalRoundsDisplay)。UI顶部显示的“累计轮次数”数值，非实时回合。
    /// Tag 10 (APLKNJEGBKF)  : 通关标志 (IsFinished)。控制卡片上是否渲染“完成”大对勾。
    /// Tag 13 (CPGOIPICPJF)  : 历史最佳记录 (BestRecord)。决定UI顶部显示的 Rank 排名数字或最高评价。
    /// Tag 14 (HNPEAPPMGAA)  : 【修正】当前挑战进度 (CurrentProgress)。控制 UI 匹配界面显示的 "X/4" 进度文本，优先级高于通关勾选。
    /// </summary>
    public List<FCIHIJLOMGA> GetChallengeList()
    {
        var challengeInfos = new List<FCIHIJLOMGA>();
        if (EnableLog) _log.Debug($"Syncing challenge list for UID: {Player?.Uid}");

        foreach (var config in GameData.BoxingClubChallengeData.Values)
        {
            var info = new FCIHIJLOMGA
            {
                ChallengeId = (uint)config.ChallengeID,
                LLFOFPNDAFG = 1,     
                APLKNJEGBKF = false, 
                CPGOIPICPJF = 0,     
                NAALCBMBPGC = 0,     
            };

            if (config.SpecialAvatarIDList != null)
            {
                foreach (var trialId in config.SpecialAvatarIDList)
                {
                    info.MDLACHDKMPH.Add(new IJKJJDHLKLB
                    {
                        AvatarId = (uint)trialId,
                        AvatarType = AvatarType.AvatarLimitType 
                    });
                }
            }

            if (CurrentChallengeId == (uint)config.ChallengeID)
            {
                info.HJMGLEMJHKG = CurrentOpponentIndex; 
                
                if (CurrentMatchEventId != 0)
                {
                    info.HLIBIJFHHPG.Add(CurrentMatchEventId);
                }
                
                if (LastMatchAvatars.Count > 0)
                {
                    info.AvatarList.AddRange(LastMatchAvatars);
                }
            }

            challengeInfos.Add(info);
        }

        return challengeInfos;
    }

    public FCIHIJLOMGA ProcessMatchRequest(MatchBoxingClubOpponentCsReq req)
    {
        if (EnableLog) _log.Info($"Matching opponent for Challenge {req.ChallengeId}...");
        
        uint selectedGroupId = 0;
        uint randomIndex = 1;
        uint targetEventId = 0;
		
        if (GameData.BoxingClubChallengeData.TryGetValue((int)req.ChallengeId, out var config))
        {
            if (config.StageGroupList != null && CurrentRoundIndex < config.StageGroupList.Count)
            {
                selectedGroupId = (uint)config.StageGroupList[CurrentRoundIndex];
            }
        }

        if (selectedGroupId != 0 && GameData.BoxingClubStageGroupData.TryGetValue((int)selectedGroupId, out var groupConfig))
        {
            int displayCount = groupConfig.DisplayEventIDList?.Count ?? 0;
            if (displayCount > 0)
			{
    		randomIndex = (uint)new Random().Next(1, displayCount + 1);
    		// 在这里加个 ! 告诉编译器你确定这个列表此时已加载
    		targetEventId = (uint)groupConfig.DisplayEventIDList![(int)randomIndex - 1];
			}
        }

        this.CurrentChallengeId = req.ChallengeId;
        this.CurrentMatchEventId = targetEventId;
        this.CurrentOpponentIndex = randomIndex;
        this.LastMatchAvatars = req.AvatarList.ToList();

        if (EnableLog) _log.Info($"Match Success: EventID {targetEventId}, Index {randomIndex}");

        var snapshot = new FCIHIJLOMGA
        {
            ChallengeId = req.ChallengeId,
            HJMGLEMJHKG = randomIndex,
            NAALCBMBPGC = 0,
            APLKNJEGBKF = false,
            LLFOFPNDAFG = 1
        };

        if (targetEventId != 0)
        {
            snapshot.HLIBIJFHHPG.Add(targetEventId); 
        }

        foreach (var item in req.MDLACHDKMPH)
        {
            snapshot.MDLACHDKMPH.Add(new IJKJJDHLKLB
            {
                AvatarId = item.AvatarId,
                AvatarType = item.AvatarType
            });
        }
        snapshot.AvatarList.AddRange(req.AvatarList);

        return snapshot;
    }

    public async ValueTask EnterBoxingClubStage(uint challengeId)
    {
        if (Player?.SceneInstance == null) return;
        if (this.CurrentChallengeId != challengeId || this.CurrentMatchEventId == 0) return;

        int actualStageId = (int)(this.CurrentMatchEventId * 10) + Player.Data.WorldLevel;
        if (EnableLog) _log.Info($"Starting Battle: StageID {actualStageId}");

        if (!GameData.StageConfigData.TryGetValue(actualStageId, out var stageConfig))
        {
            if (EnableLog) _log.Error($"StageID {actualStageId} not found!");
            return;
        }

        int turnLimit = 30;
        if (GameData.BoxingClubChallengeData.TryGetValue((int)challengeId, out var config))
        {
            turnLimit = config.ChallengeTurnLimit > 0 ? config.ChallengeTurnLimit : 30;
        }

        // --- [核心修复：构建临时阵容与插件] ---
        var avatarList = new List<AvatarSceneInfo>();
        var lineupAvatars = new List<LineupAvatarInfo>(); // 用于构建临时编队上下文

        foreach (var id in LastMatchAvatars)
        {
            BaseAvatarInfo? avatarData = (BaseAvatarInfo?)Player.AvatarManager!.GetFormalAvatar((int)id) ?? 
                                         Player.AvatarManager!.GetTrialAvatar((int)id);

            if (avatarData != null)
            {
                AvatarType type = (avatarData is SpecialAvatarInfo) ? AvatarType.AvatarTrialType : AvatarType.AvatarFormalType;
                
                // 1. 构造场景实体 (用于模型显示)
                var sceneInfo = new AvatarSceneInfo(avatarData, type, Player)
                {
                    EntityId = ++Player.SceneInstance.LastEntityId 
                };
                avatarList.Add(sceneInfo);

                // 2. 构造编队元数据 (用于 ToBattleProto 的上下文)
                lineupAvatars.Add(new LineupAvatarInfo {
                    BaseAvatarId = (int)id,
                    SpecialAvatarId = (type == AvatarType.AvatarTrialType) ? (int)id : 0
                });

                // 3. 强制满血满能进场
                avatarData.SetCurHp(10000, true);
                avatarData.SetCurSp(10000, true);
            }
        }

        // --- [学习 GridFight：装载劫持插件] ---
        // 先给 LineupManager 设置一个临时编队类型，确保 IsExtraLineup 为 true
        Player.LineupManager.SetExtraLineup(ExtraLineupType.LineupBoxingClub, lineupAvatars);
        var curLineup = Player.LineupManager.GetCurLineup();

        BattleInstance battleInstance = new(Player, curLineup!, new List<StageConfigExcel> { stageConfig })
        {
            WorldLevel = Player.Data.WorldLevel,
            EventId = (int)this.CurrentMatchEventId,
            RoundLimit = (uint)turnLimit,
            // 注入劫持选项，把我们准备好的 avatarList 传给 BattleInstance.ToProto
            BoxingClubOptions = new BattleBoxingClubOptions(avatarList, new List<uint>(), Player) 
        };

        Player.BattleInstance = battleInstance;
        
        // 发送进入战斗包 (4283)
        await Player.SendPacket(new PacketSceneEnterStageScRsp(battleInstance));

        // --- [重要：严禁调用 OnEnterStage()] ---
        // Player.SceneInstance?.OnEnterStage(); // 必须注释掉！否则会刷掉刚才生成的临时实体

        Player.QuestManager?.OnBattleStart(battleInstance);

        if (EnableLog) _log.Debug("Boxing Club Battle Started with custom hijacked lineup.");
    }
	/// <summary>
    /// 处理协议 4281 (ChooseBoxingClubResonanceCsReq)
    /// 逻辑：调用 AdvanceNextRound 推进索引，返回不带 HLIBIJFHHPG 的快照，触发下一轮抽选。
    /// </summary>
    /// <summary>
    /// 处理协议 4281 (ChooseBoxingClubResonanceCsReq)
    /// </summary>
    public FCIHIJLOMGA? ProcessChooseResonance(uint challengeId, uint resonanceId)
    {
        if (this.CurrentChallengeId != challengeId) return null;

        if (EnableLog) _log.Info($"[Boxing] 收到共鸣选择: {resonanceId}，执行晋级。");

        // --- 核心修改：记录选中的 Buff ---
        if (resonanceId != 0 && !CurrentChallengeBuffs.Contains(resonanceId))
        {
            this.CurrentChallengeBuffs.Add(resonanceId);
        }

        // 1. 调用 AdvanceNextRound：CurrentRoundIndex++，CurrentMatchEventId = 0
        this.AdvanceNextRound();

        // 2. 构造快照
        var snapshot = new FCIHIJLOMGA
        {
            ChallengeId = challengeId,
            LLFOFPNDAFG = 1, // 状态 1 代表进行中
            HNPEAPPMGAA = (uint)(this.CurrentRoundIndex + 1), // UI 显示进度，如 2/4
            APLKNJEGBKF = false
        };

        // 3. 判定是否已打完
        if (GameData.BoxingClubChallengeData.TryGetValue((int)challengeId, out var config))
        {
            int maxRounds = config.StageGroupList?.Count ?? 0;
            if (this.CurrentRoundIndex >= maxRounds)
            {
                snapshot.APLKNJEGBKF = true; // 显示大勾
                snapshot.HNPEAPPMGAA = (uint)maxRounds; 
            }
        }

        // 4. 同步阵容记忆，防止返回 UI 后角色掉线
        snapshot.AvatarList.AddRange(this.LastMatchAvatars);

        // 保持 HLIBIJFHHPG 为空，触发 UI 的“匹配对手”按钮
        return snapshot;
    }
	/// <summary>
    /// 处理协议 4294 (GiveUpBoxingClubChallengeCsReq)
    /// PCPDFJHDJCC == true: 彻底放弃，进度清零。
    /// PCPDFJHDJCC == false: 带进度的放弃，仅重置当前匹配状态。
    /// </summary>
    public FCIHIJLOMGA ProcessGiveUpChallenge(uint challengeId, bool isFullReset)
    {
        if (EnableLog) _log.Info($"[Boxing] 放弃挑战请求: ID {challengeId}, 是否彻底重置: {isFullReset}");

        if (isFullReset)
        {
            // --- 场景 A: 彻底放弃 (PCPDFJHDJCC = true) ---
            this.CurrentRoundIndex = 0;       // 轮次回到第一轮 (0)
            this.LastMatchAvatars.Clear();    // 清空选人记忆
            this.CurrentChallengeId = 0;      // 清除当前挑战激活状态
        }
        
        // --- 场景 B: 共有逻辑 (无论哪种放弃，都得清掉当前匹配到的怪) ---
        this.CurrentMatchEventId = 0;        // 清除已锁定的怪物 ID
        this.CurrentOpponentIndex = 0;       // 清除转盘位置索引

        // 构造返回给客户端的快照
        var snapshot = new FCIHIJLOMGA
        {
            ChallengeId = challengeId,
            LLFOFPNDAFG = 1,                 // 状态保持为积分赛 (1)
            // 如果是彻底重置，进度给 0；否则保持当前进度 (1-based, 所以是 index + 1)
            HNPEAPPMGAA = isFullReset ? 0 : (uint)(this.CurrentRoundIndex + 1),
            APLKNJEGBKF = false,             // 放弃肯定不是通关
            NAALCBMBPGC = 0                  // 重置累计轮次显示
        };

        // 如果只是中途退出（保留进度），要把人发回去，防止 UI 选人界面变空
        if (!isFullReset)
        {
            snapshot.AvatarList.AddRange(this.LastMatchAvatars);
        }

        return snapshot;
    }
    public void AdvanceNextRound()
    {
        if (EnableLog) _log.Info($"AdvanceRound: From {CurrentRoundIndex} to {CurrentRoundIndex + 1}");
        CurrentRoundIndex++;
        CurrentMatchEventId = 0;
        CurrentOpponentIndex = 0;
    }
}
