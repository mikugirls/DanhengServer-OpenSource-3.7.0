using EggLink.DanhengServer.Database.Lineup;
using EggLink.DanhengServer.Enums.Avatar;
using EggLink.DanhengServer.GameServer.Game.Battle;
using EggLink.DanhengServer.GameServer.Game.Battle.Custom;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup; // 必须引用以发送同步包
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.GameServer.Game.Scene.Entity;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.GameServer.Game.Scene;
namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubInstance(PlayerInstance player, uint challengeId, List<uint> avatars)
{
    private static readonly Logger _log = new("BoxingClubInstance");
    
    public PlayerInstance Player { get; } = player;
    public uint ChallengeId { get; } = challengeId;
    public List<uint> SelectedAvatars { get; } = avatars;
    public List<uint> SelectedBuffs { get; } = new();
    // --- 添加下面这一行 ---
    public uint CurrentStageGroupId { get; set; } = 0; 
    // ----------------------
    public int CurrentRoundIndex { get; set; } = 0;
    public uint CurrentMatchEventId { get; set; } = 0;
    public uint CurrentOpponentIndex { get; set; } = 0;
	public uint TotalUsedTurns { get; set; } = 0;

    /// <summary>
    /// 进入战斗：负责槽位 19 的写入、同步以及战斗场景切换
    /// </summary>
    public async ValueTask EnterStage()
    {
        if (CurrentMatchEventId == 0) 
        {
            _log.Error("[Boxing] 进入失败：未匹配对手。");
            return;
        }

        int actualStageId = (int)(CurrentMatchEventId * 10) + Player.Data.WorldLevel;
        if (!Data.GameData.StageConfigData.TryGetValue(actualStageId, out var stageConfig)) 
        {
            _log.Error($"[Boxing] 找不到 Stage 配置: {actualStageId}");
            return;
        }

        // 1. 构造详细阵容信息 (兼容试用与正式角色)
        var boxingLineup = new List<LineupAvatarInfo>();
        foreach (var id in SelectedAvatars)
        {
            var trial = Player.AvatarManager!.GetTrialAvatar((int)id);
            if (trial != null)
            {
                trial.CheckLevel(Player.Data.WorldLevel);
                boxingLineup.Add(new LineupAvatarInfo 
                { 
                    BaseAvatarId = trial.BaseAvatarId, 
                    SpecialAvatarId = (int)id 
                });
            }
            else
            {
                var formal = Player.AvatarManager.GetFormalAvatar((int)id);
                if (formal != null)
                {
                    boxingLineup.Add(new LineupAvatarInfo { BaseAvatarId = (int)id });
                }
            }
        }

        // 2. 写入并激活槽位 19 (搏击俱乐部专用编队)
        // 使用新增重载透传 List<LineupAvatarInfo>
        Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupBoxingClub, boxingLineup);
        await Player.LineupManager.SetExtraLineup(ExtraLineupType.LineupBoxingClub, false); // false 代表先不在这里发同步包

        // 3. 【核心修正】同步阵容到场景与客户端
        var curLineup = Player.LineupManager.GetCurLineup();
        if (curLineup != null)
        {
            // 同步场景中的角色实体模型
            Player.SceneInstance?.SyncLineup();
            // 发送阵容数据同步包，确保客户端 UI 拥有角色数据
            await Player.SendPacket(new PacketSyncLineupNotify(curLineup));
        }
        else
        {
            _log.Error("[Boxing] 严重错误：编队激活后无法获取当前 LineupInfo。");
            return;
        }

        // 4. 构造战斗实例
        var battleInstance = new BattleInstance(Player, curLineup, [stageConfig])
        {
            WorldLevel = Player.Data.WorldLevel,
            EventId = (int)CurrentMatchEventId,
			RoundLimit = (uint)config.ChallengeTurnLimit,
            BoxingClubOptions = new BattleBoxingClubOptions(SelectedBuffs.ToList(), Player)
        };

        Player.BattleInstance = battleInstance;

        // 5. 发送进入战斗场景协议
        await Player.SendPacket(new Server.Packet.Send.Scene.PacketSceneEnterStageScRsp(battleInstance));
        
        _log.Info($"[Boxing] 玩家 {Player.Uid} 成功进入战斗场景，已注入 {boxingLineup.Count} 名角色。");
    }

    /// <summary>
    /// 结算拦截：判断胜负并决定是否重置阵容
    /// </summary>
	/// <summary>
    /// 核心结算拦截：处理战斗结束后的逻辑分支
    /// </summary>
    public async ValueTask OnBattleEnd(PVEBattleResultCsReq req)
    {
        if (req.EndStatus == BattleEndStatus.BattleEndWin)
        {
            await HandleBattleWin();
        }
        else
        {
            await HandleBattleLoss();
        }
    }

    /// <summary>
    /// 封装：处理胜利逻辑
    /// </summary>
    private async ValueTask HandleBattleWin()
    {
        this.CurrentRoundIndex++;
        _log.Info($"[Boxing] 战斗胜利，推进至轮次索引: {this.CurrentRoundIndex}");

        // 积分赛通常为 4 轮，达到 4 意味着 0,1,2,3 四场全胜
        if (this.CurrentRoundIndex >= 4)
        {
            await FinishChallenge();
        }
        else
        {
            await ProceedToNextRound();
        }
    }

    /// <summary>
    /// 封装：处理失败逻辑
    /// </summary>
    private async ValueTask HandleBattleLoss()
    {
        _log.Info($"[Boxing] 挑战失败，正在重置状态。");
        // 失败时必须释放额外阵容，否则玩家在大世界会被锁死在 Slot 19
        await Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupNone);
        
        if (Player.BoxingClubManager != null) 
            Player.BoxingClubManager.ChallengeInstance = null;
    }

    /// <summary>
    /// 封装方法 1：推进至下一轮 (转盘动画与数据更新)
    /// </summary>
    private async ValueTask ProceedToNextRound()
    {
        if (Data.GameData.BoxingClubChallengeData.TryGetValue((int)this.ChallengeId, out var config))
        {
            // 保持组 ID 一致，避免客户端重载场景
            uint persistentGroupId = (uint)config.StageGroupList[0];
            this.CurrentStageGroupId = persistentGroupId;

            if (Data.GameData.BoxingClubStageGroupData.TryGetValue((int)persistentGroupId, out var groupConfig))
            {
                var eventPool = groupConfig.DisplayEventIDList;
                if (eventPool != null && eventPool.Count > 0)
                {
                    // 切换下场战斗怪物 ID
                    int poolIndex = Math.Min(this.CurrentRoundIndex, eventPool.Count - 1);
                    this.CurrentMatchEventId = (uint)eventPool[poolIndex];

                    // 构造并发送同步快照
                    var snapshot = Player.BoxingClubManager!.ConstructSnapshot(this);
                    snapshot.HJMGLEMJHKG = persistentGroupId; 
                    snapshot.HNPEAPPMGAA = (uint)this.CurrentRoundIndex;
                    
                    snapshot.HLIBIJFHHPG.Clear();
                    snapshot.HLIBIJFHHPG.AddRange(eventPool.Select(x => (uint)x));

                    await Player.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));
                    await Player.SceneInstance!.SyncLineup();

                    _log.Info($"[Boxing] 下一轮就绪: HN={snapshot.HNPEAPPMGAA}, NextMonster={this.CurrentMatchEventId}");
                }
            }
        }
    }

    /// <summary>
    /// 封装方法 2：挑战完美达成结算
    /// </summary>
    /// <summary>
    /// 封装：通关结算 (发奖、归零进度、标记完成、释放阵容)
    /// </summary>
    private async ValueTask FinishChallenge()
    {
        _log.Info($"[Boxing] 最终通关结算开始: ChallengeId {this.ChallengeId}, 总回合: {this.TotalUsedTurns}");

        if (Data.GameData.BoxingClubChallengeData.TryGetValue((int)this.ChallengeId, out var config))
        {
            // 1. 发放奖励逻辑 (调用你的 InventoryManager.HandleReward)
            // 增加对 InventoryManager 的空检查以修复编译警告
            if (Player.InventoryManager != null)
            {
                // 获取首通奖励并入库 (notify: false 避免弹出通用的小黑框提示)
                var resItems = await Player.InventoryManager.HandleReward(config.FirstPassRewardID, notify: false, sync: true);

                // 2. 构造通关大图通知 (4224 - BoxingClubRewardScNotify)
                var rewardNotify = new BoxingClubRewardScNotify 
                {
                    ChallengeId = this.ChallengeId,
                    IsWin = true,
                    NAALCBMBPGC = this.TotalUsedTurns, // 将总回合数作为评价数据发送
                    Reward = new ItemList()
                };

                // 将奖励物品填充进大图展示列表
                foreach (var item in resItems) 
                {
                    // [修正] 根据 Item.proto，字段名应为 Num
                    rewardNotify.Reward.ItemList_.Add(new Item 
                    { 
                        ItemId = (uint)item.ItemId, 
                        Num = (uint)item.Count 
                    });
                }
                
                // 发送大图通知
                await Player.SendPacket(new PacketBoxingClubRewardScNotify(rewardNotify));
            }

            // 3. 状态同步：进度归零、点亮主界面大勾勾 (4244)
            await Player.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(new FCIHIJLOMGA 
            {
                ChallengeId = this.ChallengeId,
                HNPEAPPMGAA = 0,    // 进度重置为 0
                APLKNJEGBKF = true, // 标记挑战已完成
                LLFOFPNDAFG = 1,    // 赛季 ID (Tag 8)
                NAALCBMBPGC = this.TotalUsedTurns // 同步实时累计回合 (Tag 9)
            }));
        }

        // 4. 清理工作：释放 Slot 19 编队锁定并销毁实例
        if (Player.LineupManager != null)
        {
            await Player.LineupManager.SetExtraLineup(ExtraLineupType.LineupNone);
        }
        
        if (Player.BoxingClubManager != null) 
        {
            Player.BoxingClubManager.ChallengeInstance = null;
        }

        _log.Info($"[Boxing] 挑战 {this.ChallengeId} 已销毁，玩家阵容已恢复正常。");
    }
 
   	
	public void OnBattleStart(BattleInstance battle)
	{
    // 关键：在这里挂钩 OnBattleEnd 方法
    // 这样当战斗结束时，会跑你那个“胜利则发送 4244 更新包”的逻辑，而不是直接回大世界
    battle.OnBattleEnd += async (inst, res) => await this.OnBattleEnd(res);
    _log.Info($"[Boxing] 战斗实例 {battle.BattleId} 已被超级联赛逻辑接管。");
	}
}
