using EggLink.DanhengServer.Database.Lineup;
using EggLink.DanhengServer.Enums.Avatar;
using EggLink.DanhengServer.GameServer.Game.Battle;
using EggLink.DanhengServer.GameServer.Game.Battle.Custom;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubInstance(PlayerInstance player, uint challengeId, List<uint> avatars)
{
    private static readonly Logger _log = new("BoxingClubInstance");
    
    public PlayerInstance Player { get; } = player;
    public uint ChallengeId { get; } = challengeId;
    public List<uint> SelectedAvatars { get; } = avatars;
    public List<uint> SelectedBuffs { get; } = new();
    
    public int CurrentRoundIndex { get; set; } = 0;
    public uint CurrentMatchEventId { get; set; } = 0;
    public uint CurrentOpponentIndex { get; set; } = 0;

    /// <summary>
    /// 进入战斗：负责槽位 19 的写入、激活以及战斗实例构造
    /// </summary>
    public async ValueTask EnterStage()
    {
        if (CurrentMatchEventId == 0) return;

        // 1. 获取 Stage 配置 (EventID * 10 + 世界等级)
        int actualStageId = (int)(CurrentMatchEventId * 10) + Player.Data.WorldLevel;
        if (!Data.GameData.StageConfigData.TryGetValue(actualStageId, out var stageConfig))
        {
            _log.Error($"无法找到 Stage 配置: {actualStageId}");
            return;
        }

        // 2. 准备额外阵容 (Slot 19)
        var boxingLineup = new List<LineupAvatarInfo>();
        foreach (var id in SelectedAvatars)
        {
            var trial = Player.AvatarManager!.GetTrialAvatar((int)id);
            // 关键：强制刷新试用角色的数值，防止等级为 0 导致 UI 不显示
            trial?.CheckLevel(Player.Data.WorldLevel);

            boxingLineup.Add(new LineupAvatarInfo
            {
                BaseAvatarId = trial?.BaseAvatarId ?? (int)id,
                SpecialAvatarId = trial != null ? (int)id : 0
            });
        }

        // 写入并激活槽位 19 (LineupBoxingClub)
        Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupBoxingClub, boxingLineup);
        await Player.LineupManager.SetExtraLineup(ExtraLineupType.LineupBoxingClub);

        // 3. 构造战斗
        var battleInstance = new BattleInstance(Player, Player.LineupManager.GetCurLineup()!, [stageConfig])
        {
            WorldLevel = Player.Data.WorldLevel,
            EventId = (int)CurrentMatchEventId,
            // 注入累加的 Buff (SelectedBuffs)
            BoxingClubOptions = new BattleBoxingClubOptions(SelectedBuffs.ToList(), Player)
        };

        Player.BattleInstance = battleInstance;
        
        // 4. 发送进入战斗协议
        await Player.SendPacket(new Server.Packet.Send.Scene.PacketSceneEnterStageScRsp(battleInstance));
        Player.QuestManager?.OnBattleStart(battleInstance);
        
        _log.Info($"[Boxing] 玩家 {Player.Uid} 进入战斗，轮次索引: {CurrentRoundIndex}");
    }

    /// <summary>
    /// 结算拦截：判断胜负并决定是否重置阵容
    /// </summary>
    public async ValueTask OnBattleEnd(PVEBattleResultCsReq req)
    {
        if (req.EndStatus == BattleEndStatus.BattleEndWin)
        {
            // 胜利：清空匹配状态
            CurrentMatchEventId = 0;
            CurrentOpponentIndex = 0;
            _log.Info($"[Boxing] 战斗胜利，轮次 {CurrentRoundIndex} 结束。");
        }
        else
        {
            // 失败或退出：强制切回大世界阵容 (None)，并销毁本局实例
            _log.Info($"[Boxing] 战斗中止/失败，正在重置阵容并销毁实例。");
            await Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupNone);
            
            // 修正：直接访问 Player.BoxingClubManager 属性
            if (Player.BoxingClubManager != null) 
            {
                Player.BoxingClubManager.ChallengeInstance = null;
            }
        }
    }
}
