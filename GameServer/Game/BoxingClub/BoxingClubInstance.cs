using EggLink.DanhengServer.Database.Lineup;
using EggLink.DanhengServer.Enums.Avatar;
using EggLink.DanhengServer.GameServer.Game.Battle;
using EggLink.DanhengServer.GameServer.Game.Battle.Custom;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup; 
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
    
    public uint CurrentStageGroupId { get; set; } = 0; 
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

        if (!Data.GameData.BoxingClubChallengeData.TryGetValue((int)this.ChallengeId, out var config))
        {
            _log.Error($"[Boxing] 找不到挑战配置: {this.ChallengeId}");
            return;
        }
		// 1. 使用模式匹配直接尝试获取配置并判定非空
		if (Data.GameData.BoxingClubStageData.TryGetValue((int)this.CurrentMatchEventId, out var stageBuffConfig) && stageBuffConfig is not null)
		{
		// 此时编译器完全确定 stageBuffConfig 不为 null
		if (stageBuffConfig.BuffID != 0 && !SelectedBuffs.Contains((uint)stageBuffConfig.BuffID))
		{
        SelectedBuffs.Add((uint)stageBuffConfig.BuffID);
        _log.Info($"[Boxing] 自动注入 Buff: {stageBuffConfig.BuffID}");
		}
		}
		else
		{
		_log.Warn($"[Boxing] 未找到 EventID {this.CurrentMatchEventId} 的 Buff 配置。");
		}
		
        int actualStageId = (int)(CurrentMatchEventId * 10) + Player.Data.WorldLevel;
        if (!Data.GameData.StageConfigData.TryGetValue(actualStageId, out var stageConfig)) 
        {
            _log.Error($"[Boxing] 找不到 Stage 配置: {actualStageId}");
            return;
        }
		if (Player.SceneInstance != null)
		{
        Player.SceneInstance.GameModeType = EggLink.DanhengServer.Enums.Scene.GameModeTypeEnum.ChallengeActivity;
        _log.Info($"[Boxing] 已将场景模式切换为: {Player.SceneInstance.GameModeType}");
		}
        // 1. 构造详细阵容信息 (兼容试用与正式角色)
        var boxingLineup = new List<LineupAvatarInfo>();
        foreach (var id in SelectedAvatars)
        {
            var trial = Player.AvatarManager?.GetTrialAvatar((int)id);
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
                var formal = Player.AvatarManager?.GetFormalAvatar((int)id);
                if (formal != null)
                {
                    boxingLineup.Add(new LineupAvatarInfo { BaseAvatarId = (int)id });
                }
            }
        }

        // 2. 写入并激活槽位 19
        Player.LineupManager?.SetExtraLineup(ExtraLineupType.LineupBoxingClub, boxingLineup);
        await (Player.LineupManager?.SetExtraLineup(ExtraLineupType.LineupBoxingClub, false) ?? ValueTask.CompletedTask);

        // 3. 同步阵容到场景与客户端
        var curLineup = Player.LineupManager?.GetCurLineup();
        if (curLineup != null)
        {
            Player.SceneInstance?.SyncLineup();
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
            RoundLimit = config.ChallengeTurnLimit,
            BoxingClubOptions = new BattleBoxingClubOptions(SelectedBuffs.ToList(), Player)
        };

        Player.BattleInstance = battleInstance;

        // 5. 发送进入战斗场景协议
        await Player.SendPacket(new Server.Packet.Send.Scene.PacketSceneEnterStageScRsp(battleInstance));
        
        _log.Info($"[Boxing] 玩家 {Player.Uid} 成功进入战斗场景。回合限制: {config.ChallengeTurnLimit}");
    }

    /// <summary>
    /// 核心结算拦截：处理战斗结束后的逻辑分支
    /// </summary>
    public async ValueTask OnBattleEnd(PVEBattleResultCsReq req)
    {
        // 判定：只有胜利才进行回合累加
        if (req.EndStatus == BattleEndStatus.BattleEndWin)
        {
            if (req.Stt != null)
            {
                this.TotalUsedTurns += req.Stt.RoundCnt;
            }
            await HandleBattleWin();
        }
        else
        {
            await HandleBattleLoss();
        }
    }
private async ValueTask HandleBattleWin()
{
    this.CurrentRoundIndex++;
    _log.Info($"[Boxing] 战斗胜利，进度推至: {this.CurrentRoundIndex}");

    // 赛季 2 判断 (ChallengeId >= 6)
    bool isSeason2 = this.ChallengeId >= 6;

    int totalRounds = 0;
    if (Data.GameData.BoxingClubStageGroupData.TryGetValue((int)this.CurrentStageGroupId, out var groupConfig))
    {
        totalRounds = groupConfig.EventIDList?.Count ?? 0;
    }

    if (this.CurrentRoundIndex >= totalRounds && totalRounds > 0)
    {
        await FinishChallenge();
    }
    else
    {
        if (isSeason2)
        {
            // 【核心改动】赛季 2 胜利后，只同步进度快照，不自动匹配
            // 客户端收到进度增加 (Tag 14) 后，会自动根据配置弹出三选一 UI
            var snapshot = Player.BoxingClubManager?.ConstructSnapshot(this);
            if (snapshot != null)
            {
                await Player.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));
            }
            _log.Info("[Boxing] 赛季 2：等待客户端发起共鸣选择(4281)...");
        }
        else
        {
            // 赛季 1：保持原样，直接进入下一轮匹配并推送到转盘
            await ProceedToNextRound();
        }
    }
}

    private async ValueTask HandleBattleLoss()
    {
        _log.Info($"[Boxing] 挑战失败，正在重置状态。");
        await (Player.LineupManager?.SetExtraLineup(ExtraLineupType.LineupNone) ?? ValueTask.CompletedTask);
        
        if (Player.BoxingClubManager != null) 
            Player.BoxingClubManager.ChallengeInstance = null;
    }

    private async ValueTask ProceedToNextRound()
{
    // 依然使用当前的 GroupId，不要去重新拿索引 0，因为组 ID 是恒定的
    if (Data.GameData.BoxingClubStageGroupData.TryGetValue((int)this.CurrentStageGroupId, out var groupConfig))
    {
        // 1. 更新下一场战斗的 EventID (用于下一场进入战斗时的 StageID 计算)
        // 这里的 EventIDList 是顺序对应的：Index 0 是第一关，Index 1 是第二关...
        if (groupConfig.EventIDList != null && this.CurrentRoundIndex < groupConfig.EventIDList.Count)
        {
            this.CurrentMatchEventId = (uint)groupConfig.EventIDList[this.CurrentRoundIndex];
        }

        // 2. 构造通知包 (进位演出)
        var snapshot = Player.BoxingClubManager?.ConstructSnapshot(this);
        if (snapshot != null)
        {
            // 确保同步最新的回合数和进度指针
            snapshot.HJMGLEMJHKG = this.CurrentStageGroupId; 
            snapshot.HNPEAPPMGAA = (uint)this.CurrentRoundIndex; // 此时已经是 1, 2...
            snapshot.NAALCBMBPGC = this.TotalUsedTurns;
            
            // 必须重新下发全量展示池，否则转盘没燃料
            if (groupConfig.DisplayEventIDList != null)
            {
                snapshot.HLIBIJFHHPG.Clear();
                snapshot.HLIBIJFHHPG.AddRange(groupConfig.DisplayEventIDList.Select(x => (uint)x));
            }

            await Player.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));
        }
        
        Player.SceneInstance?.SyncLineup();
        _log.Info($"[Boxing] 进位成功。当前进度: {this.CurrentRoundIndex}, 下一场 EventId: {this.CurrentMatchEventId}");
    }
}

    /// <summary>
    /// 封装：通关结算 (持久化数据库、发奖、释放环境)
    /// </summary>
    private async ValueTask FinishChallenge()
    {
        _log.Info($"[Boxing] 最终通关！ChallengeId: {this.ChallengeId}, 总回合: {this.TotalUsedTurns}");

        if (Data.GameData.BoxingClubChallengeData.TryGetValue((int)this.ChallengeId, out var config))
        {
            // --- 数据库持久化安全处理 ---
            var db = Player.BoxingClubData;
            if (db != null)
            {
                if (!db.Challenges.TryGetValue((int)this.ChallengeId, out var info))
                {
                    info = new EggLink.DanhengServer.Database.BoxingClub.BoxingClubInfo { ChallengeId = (int)this.ChallengeId };
                    db.Challenges[(int)this.ChallengeId] = info;
                }

                info.IsFinished = true;

                int currentTotal = (int)this.TotalUsedTurns;
                if (info.MinRounds <= 0 || currentTotal < info.MinRounds)
                {
                    info.MinRounds = currentTotal;
                }

                // 核心修复 CS8604: 先判定 null 再调用 ToList
                var avatars = Player.LineupManager?.GetCurLineup()?.BaseAvatars;
                if (avatars != null)
                {
                    info.Lineup = avatars.ToList();
                }

                Database.DatabaseHelper.UpdateInstance(db);
            }

            // 发奖逻辑
            if (Player.InventoryManager != null)
            {
                var resItems = await Player.InventoryManager.HandleReward(config.FirstPassRewardID, notify: false, sync: true);
                
                var rewardNotify = new BoxingClubRewardScNotify 
                {
                    ChallengeId = this.ChallengeId,
                    IsWin = true,
                    NAALCBMBPGC = this.TotalUsedTurns, 
                    Reward = new ItemList()
                };

                if (resItems != null)
                {
                    foreach (var item in resItems) 
                    {
                        rewardNotify.Reward.ItemList_.Add(new Item 
                        { 
                            ItemId = (uint)item.ItemId, 
                            Num = (uint)item.Count 
                        });
                    }
                }
                await Player.SendPacket(new PacketBoxingClubRewardScNotify(rewardNotify));
            }

            // 获取数据库记录中的历史最快回合，消除 CS8602 警告
            uint minRounds = 0;
            if (Player.BoxingClubData?.Challenges.TryGetValue((int)this.ChallengeId, out var finalInfo) == true)
            {
                minRounds = (uint)finalInfo.MinRounds;
            }

            // 最终状态同步协议 (4244)
            await Player.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(new FCIHIJLOMGA 
            {
               ChallengeId = this.ChallengeId,
    		   // HNPEAPPMGAA 设为 0 或不传，配合组 ID 缺失，让客户端回到选人主界面
    		   APLKNJEGBKF = true, 
    		   LLFOFPNDAFG = 1,
    			NAALCBMBPGC = this.TotalUsedTurns,
    			CPGOIPICPJF = minRounds 
            }));
        }

      // A. 将场景模式切回 Town，否则后续副本战斗会被误判为活动
        if (Player.SceneInstance != null)
        {
            Player.SceneInstance.GameModeType = EggLink.DanhengServer.Enums.Scene.GameModeTypeEnum.Town;
            _log.Info($"[Boxing] 模式重置：场景 GameModeType 已切回 Town。");
        }

        // B. 清理 Slot 19 (活动阵容)，让大世界恢复使用常规 Lineup
        if (Player.LineupManager != null)
        {
            await Player.LineupManager.SetExtraLineup(ExtraLineupType.LineupNone);
            Player.SceneInstance?.SyncLineup(); // 强制刷新大世界模型
            _log.Info($"[Boxing] 阵容清理：已移除活动编队，恢复常规编队。");
        }
        
        // C. 销毁活动实例
        if (Player.BoxingClubManager != null)
            Player.BoxingClubManager.ChallengeInstance = null;

        _log.Info($"[Boxing] 挑战 {this.ChallengeId} 结算完成，环境已彻底清理。");

        _log.Info($"[Boxing] 挑战 {this.ChallengeId} 结算完成并已清理。");
    }
 
    public void OnBattleStart(BattleInstance battle)
    {
        battle.OnBattleEnd += async (inst, res) => await this.OnBattleEnd(res);
        _log.Info($"[Boxing] 战斗接管成功。BattleId: {battle.BattleId}");
    }
}
