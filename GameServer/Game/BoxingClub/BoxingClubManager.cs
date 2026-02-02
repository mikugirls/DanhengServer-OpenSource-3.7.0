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

namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubManager(PlayerInstance player) : BasePlayerManager(player)
{
    // --- 全局调试日志开关 ---
    public static bool EnableLog = true; 

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
    /// 获取挑战列表协议数据重构
    /// 混淆类 FCIHIJLOMGA 字段业务映射说明:
    /// Tag 2  (ChallengeId)  : 关卡 ID (羽量级/重量级等)。
    /// Tag 4  (HJMGLEMJHKG)  : 【关键】对手位置索引 (1-based)。填 1 就是第一个怪，填 10 就是第十个。
    /// Tag 1  (HLIBIJFHHPG)  : 【核心】本次匹配到的 EventID 列表 (repeated uint)。
    /// Tag 10 (APLKNJEGBKF)  : 是否已通关 (IsFinished)。
    /// Tag 13 (CPGOIPICPJF)  : 历史最快回合数 (MinRounds)，决定评价等级。
    /// Tag 9  (NAALCBMBPGC)  : 当前挑战实时累计回合数 (TotalUsedTurns)。
    /// Tag 8  (LLFOFPNDAFG)  : 开启状态 (Status)，填 1 解锁。
    /// Tag 6  (MDLACHDKMPH)  : 限定试用角色池 (SpecialAvatarList)，解决选人界面无头像。
    /// Tag 3  (AvatarList)   : 选人记忆列表 (SelectedAvatars)，解决已选角色离队。
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

        BattleInstance battleInstance = new(Player, Player.LineupManager!.GetCurLineup()!, new List<StageConfigExcel> { stageConfig })
        {
            WorldLevel = Player.Data.WorldLevel,
            EventId = (int)this.CurrentMatchEventId,
            CustomLevel = 10 + (Player.Data.WorldLevel * 10),
            MappingInfoId = 0, 
            StaminaCost = 0
        };

        var avatarList = new List<AvatarSceneInfo>();
        foreach (var id in LastMatchAvatars)
        {
            BaseAvatarInfo? avatarData = (BaseAvatarInfo?)Player.AvatarManager!.GetFormalAvatar((int)id) ?? 
                                         Player.AvatarManager!.GetTrialAvatar((int)id);

            if (avatarData != null)
            {
                AvatarType type = (avatarData is SpecialAvatarInfo) ? AvatarType.AvatarTrialType : AvatarType.AvatarFormalType;
                var sceneInfo = new AvatarSceneInfo(avatarData, type, Player)
                {
                    EntityId = ++Player.SceneInstance.LastEntityId 
                };
                avatarList.Add(sceneInfo);
            }
        }
        
        battleInstance.AvatarInfo = avatarList;
        Player.BattleInstance = battleInstance;
        
        await Player.SendPacket(new PacketSceneEnterStageScRsp(battleInstance));
        Player.SceneInstance?.OnEnterStage();
		Player.QuestManager?.OnBattleStart(battleInstance);
        
        if (EnableLog) _log.Debug("Stage packet sent, entering battle scene...");
    }

    public void AdvanceNextRound()
    {
        if (EnableLog) _log.Info($"AdvanceRound: From {CurrentRoundIndex} to {CurrentRoundIndex + 1}");
        CurrentRoundIndex++;
        CurrentMatchEventId = 0;
        CurrentOpponentIndex = 0;
    }
}
