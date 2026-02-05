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
			RoundLimit = 30, // 搏击俱乐部的默认轮次限制
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
   public async ValueTask OnBattleEnd(PVEBattleResultCsReq req)
{
    if (req.EndStatus == BattleEndStatus.BattleEndWin)
    {
        // 1. 逻辑推进
        CurrentMatchEventId = 0;
        CurrentOpponentIndex = 0;
        CurrentRoundIndex++; 

        _log.Info($"[Boxing] 战斗胜利，轮次推进至 {CurrentRoundIndex}。");

        if (Player.BoxingClubManager != null)
        {
            // 2. 构造快照
            var snapshot = Player.BoxingClubManager.ConstructSnapshot(this);
            
            // 3. 【核心修正】安全的位置维持
            // 既然 BaseGameEntity 没有 Motion，我们直接维持 PlayerData 里的现有位置即可。
            // 只要不触发 BattleManager 里的 EnterScene 或 MoveTo，玩家就会停在原地。
            if (Player.Data.Pos != null) 
            {
                Player.Data.Pos = Player.Data.Pos; // 显式维持引用
            }

            // 4. 发送更新包 (4244) - 触发选 Buff UI
            await Player.SendPacket(new PacketBoxingClubChallengeUpdateScNotify(snapshot));

            // 5. 【核心修正】强制刷新场景实体
            // 这一步能让客户端清理掉刚刚战斗产生的怪物残留，并重新渲染角色
            if (Player.SceneInstance != null)
            {
                await Player.SceneInstance.SyncLineup();
            }
        }
    }
    else
    {
        // 失败逻辑...
        await Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupNone);
        if (Player.BoxingClubManager != null) Player.BoxingClubManager.ChallengeInstance = null;
    }
}
	public void OnBattleStart(BattleInstance battle)
	{
    // 关键：在这里挂钩 OnBattleEnd 方法
    // 这样当战斗结束时，会跑你那个“胜利则发送 4244 更新包”的逻辑，而不是直接回大世界
    battle.OnBattleEnd += async (inst, res) => await this.OnBattleEnd(res);
    _log.Info($"[Boxing] 战斗实例 {battle.BattleId} 已被超级联赛逻辑接管。");
	}
}