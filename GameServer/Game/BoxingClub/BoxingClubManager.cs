using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Battle;      // 解决 BattleInstance
using EggLink.DanhengServer.GameServer.Game.Scene.Entity; // 解决 AvatarSceneInfo
using EggLink.DanhengServer.Database.Avatar;            // 解决 BaseAvatarInfo
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Scene; // 解决 PacketSceneEnterStageScRsp
namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubManager(PlayerInstance player) : BasePlayerManager(player)
{
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
    /// Tag 15 (HJMGLEMJHKG)  : 【关键】对手位置索引 (1-based)。填 1 就是第一个怪，填 10 就是第十个。
    /// Tag 12 (HLIBIJFHHPG)  : 【核心】本次匹配到的 EventID 列表 (repeated uint)。
    /// Tag 10 (APLKNJEGBKF)  : 是否已通关 (IsFinished)。
    /// Tag 13 (CPGOIPICPJF)  : 历史最快回合数 (MinRounds)，决定评价等级。
    /// Tag 9  (NAALCBMBPGC)  : 当前挑战实时累计回合数 (TotalUsedTurns)。
    /// Tag 8  (LLFOFPNDAFG)  : 开启状态 (Status)，填 1 解锁。
    /// Tag 11 (MDLACHDKMPH)  : 限定试用角色池 (SpecialAvatarList)，解决选人界面无头像。
    /// Tag 6  (AvatarList)   : 选人记忆列表 (SelectedAvatars)，解决已选角色离队。
    /// </summary>
    public List<FCIHIJLOMGA> GetChallengeList()
    {
        var challengeInfos = new List<FCIHIJLOMGA>();

        foreach (var config in GameData.BoxingClubChallengeData.Values)
        {
            var info = new FCIHIJLOMGA
            {
                ChallengeId = (uint)config.ChallengeID,
                LLFOFPNDAFG = 1,     // 默认开启状态
                APLKNJEGBKF = false, // TODO: 从持久化数据库读取
                CPGOIPICPJF = 0,     // 历史最高评价回合数
                NAALCBMBPGC = 0,     // 当前已消耗回合数
            };

            // 1. 注入试用角色池 (让选人界面能看到那些带“试”字的角色)
            if (config.SpecialAvatarIDList != null)
            {
                foreach (var trialId in config.SpecialAvatarIDList)
                {
                    info.MDLACHDKMPH.Add(new IJKJJDHLKLB
                    {
                        AvatarId = (uint)trialId,
                        AvatarType = AvatarType.AvatarLimitType // 必须填 2
                    });
                }
            }

            // 2. 状态同步：如果该关卡正在进行中，回显随机结果和记忆阵容
            if (CurrentChallengeId == (uint)config.ChallengeID)
            {
                // 回显当前轮次随机到的 Display 索引
                info.HJMGLEMJHKG = CurrentOpponentIndex; 
                
                // 回显具体的 EventID 列表，确保 UI 弱点与战斗一致
                if (CurrentMatchEventId != 0)
                {
                    info.HLIBIJFHHPG.Add(CurrentMatchEventId);
                }
                
                // 【核心修复】将选中的 4 个英雄 ID 塞回列表，UI 才不会显示“离队”
                if (LastMatchAvatars.Count > 0)
                {
                    info.AvatarList.AddRange(LastMatchAvatars);
                }
            }

            challengeInfos.Add(info);
        }

        return challengeInfos;
    }

    /// <summary>
    /// 处理匹配请求并构造回显快照
    /// </summary>
    public FCIHIJLOMGA ProcessMatchRequest(MatchBoxingClubOpponentCsReq req)
    {
        uint selectedGroupId = 0;
        uint randomIndex = 1;
        uint targetEventId = 0;

        // 1. 动态寻组：根据当前轮次 CurrentRoundIndex (0, 1, 2...) 找到对应的 StageGroupID
        if (GameData.BoxingClubChallengeData.TryGetValue((int)req.ChallengeId, out var config))
        {
            if (config.StageGroupList != null && CurrentRoundIndex < config.StageGroupList.Count)
            {
                selectedGroupId = (uint)config.StageGroupList[CurrentRoundIndex];
            }
        }

        // 2. 动态随机：在 DisplayEventIDList 里执行随机抽选
        if (selectedGroupId != 0 && GameData.BoxingClubStageGroupData.TryGetValue((int)selectedGroupId, out var groupConfig))
        {
            // 基于客户端展示池 (DisplayEventIDList) 的长度进行随机
            int displayCount = groupConfig.DisplayEventIDList?.Count ?? 0;
            if (displayCount > 0)
            {
                // 生成 1 到 displayCount 之间的随机索引
                randomIndex = (uint)new Random().Next(1, displayCount + 1);
                // 提取该索引对应的真实 EventID (用于战斗加载)
                targetEventId = (uint)groupConfig.DisplayEventIDList[(int)randomIndex - 1];
            }
        }

        // 3. 记录本次匹配的快照状态
        this.CurrentChallengeId = req.ChallengeId;
        this.CurrentMatchEventId = targetEventId;
        this.CurrentOpponentIndex = randomIndex;
        this.LastMatchAvatars = req.AvatarList.ToList();

        // 4. 构造 ScRsp 回传快照
        var snapshot = new FCIHIJLOMGA
        {
            ChallengeId = req.ChallengeId,
            HJMGLEMJHKG = randomIndex, // 告诉客户端：显示 Display 列表里的第 X 个对手
            NAALCBMBPGC = 0,
            APLKNJEGBKF = false,
            LLFOFPNDAFG = 1
        };

        // 必须下发选中的 EventID，否则战斗逻辑会失去目标
        if (targetEventId != 0)
        {
            snapshot.HLIBIJFHHPG.Add(targetEventId); 
        }

        // 【解决离队】镜像回传请求中的角色数据，锁定 UI 阵容
        snapshot.MDLACHDKMPH.AddRange(req.MDLACHDKMPH);
        snapshot.AvatarList.AddRange(req.AvatarList);

        return snapshot;
    }

    /// <summary>
    /// 胜利结算后调用：推进轮次下标
    /// </summary>
    public void AdvanceNextRound()
    {
        CurrentRoundIndex++;
        // 每一波结束后重置随机状态
        CurrentMatchEventId = 0;
        CurrentOpponentIndex = 0;
    }
    /// <summary>
    /// 【核心处理】封装战斗启动逻辑
    /// 公式 1: StageID = CurrentMatchEventId * 10 + WorldLevel
    /// 公式 2: MonsterLevel = 10 + WorldLevel * 10
    /// </summary>
    public async ValueTask EnterBoxingClubStage(uint challengeId)
{
    // 1. 校验
    if (this.CurrentChallengeId != challengeId || this.CurrentMatchEventId == 0) return;

    // 2. 【核心公式】StageID = EventID * 10 + 均衡等级
    int actualStageId = (int)(this.CurrentMatchEventId * 10) + Player.Data.WorldLevel;
    
    // 3. 获取 Stage 配置
    if (!GameData.StageConfigData.TryGetValue(actualStageId, out var stageConfig)) return;

    // 4. 初始化 BattleInstance
    // 此时传入计算好的 stageConfig，Stages 列表就不是空的了
    BattleInstance battleInstance = new(Player, Player.LineupManager!.GetCurLineup()!, [stageConfig])
    {
        WorldLevel = Player.Data.WorldLevel,
        EventId = (int)this.CurrentMatchEventId,
        CustomLevel = 10 + (Player.Data.WorldLevel * 10), // 怪物等级公式
        MappingInfoId = 0, // 严禁走 MappingInfo 逻辑
        StaminaCost = 0
    };

    // 5. 注入匹配时选好的 4 人阵容 (解决离队 Bug)
    var avatarList = new List<AvatarSceneInfo>();
    foreach (var id in LastMatchAvatars)
    {
        BaseAvatarInfo? avatarData = (BaseAvatarInfo?)Player.AvatarManager!.GetFormalAvatar((int)id) ?? 
                                     Player.AvatarManager!.GetTrialAvatar((int)id);
        if (avatarData != null)
        {
            avatarList.Add(new AvatarSceneInfo(avatarData.AvatarInfo, avatarData.AvatarType, Player)
            {
                EntityId = ++Player.SceneInstance!.LastEntityId 
            });
        }
    }
    battleInstance.AvatarInfo = avatarList;

    // 6. 挂载并发送你说的那个 ScRsp
    Player.BattleInstance = battleInstance;
    
    // 【关键步骤】发送包含 BattleInfo 的 PacketSceneEnterStageScRsp
    await Player.SendPacket(new PacketSceneEnterStageScRsp(battleInstance));

    // 触发 Quest 等系统监听
    Player.QuestManager!.OnBattleStart(battleInstance);
}
}
