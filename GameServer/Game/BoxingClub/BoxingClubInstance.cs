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
                this.TotalUsedTurns += req.Stt.TotalBattleTurns;
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
        _log.Info($"[Boxing] 战斗胜利，推进至轮次索引: {this.CurrentRoundIndex}");

        if (this.CurrentRoundIndex >= 4)
        {
            await FinishChallenge();
        }
        else
        {
            await ProceedToNextRound();
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
        if (Data.GameData.BoxingClubChallengeData.TryGetValue((int)this.ChallengeId, out var config))
        {
            uint persistentGroupId = (uint)config.StageGroupList[0];
            this.CurrentStageGroupId = persistentGroupId;

            if (Data.GameData.BoxingClubStageGroupData.TryGetValue((int)persistentGroupId, out var groupConfig))
            {
                var eventPool = groupConfig.DisplayEventIDList;
                if (eventPool != null && eventPool.Count > 0)
                {
                    int poolIndex = Math.Min(this.CurrentRoundIndex, eventPool.Count - 1);
                    this.CurrentMatchEventId = (uint)eventPool[poolIndex];

                    var snapshot = Player.BoxingClubManager?.ConstructSnapshot(this);
                    if (snapshot != null)
                    {
                        snapshot.HJMGLEMJHKG = persistentGroupId; 
                        snapshot.HNPEAPPMGAA = (uint)this.CurrentRoundIndex;
                        snapshot.NAALCBMBPGC = this.TotalUsedTurns; // 同步当前累计回合
                        
                        snapshot.HLIBIJFHHPG.Clear();
                        snapshot.HLIBIJFHHPG.AddRange(eventPool.Select(x => (uint)x));

                        await Player.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));
                    }
                    Player.SceneInstance?.SyncLineup();
                }
            }
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
                HNPEAPPMGAA = 0,    
                APLKNJEGBKF = true, 
                LLFOFPNDAFG = 1,
                NAALCBMBPGC = this.TotalUsedTurns,
                CPGOIPICPJF = minRounds 
            }));
        }

        // 清理 Slot 19
        await (Player.LineupManager?.SetExtraLineup(ExtraLineupType.LineupNone) ?? ValueTask.CompletedTask);
        
        if (Player.BoxingClubManager != null)
            Player.BoxingClubManager.ChallengeInstance = null;

        _log.Info($"[Boxing] 挑战 {this.ChallengeId} 结算完成并已清理。");
    }
 
    public void OnBattleStart(BattleInstance battle)
    {
        battle.OnBattleEnd += async (inst, res) => await this.OnBattleEnd(res);
        _log.Info($"[Boxing] 战斗接管成功。BattleId: {battle.BattleId}");
    }
}
