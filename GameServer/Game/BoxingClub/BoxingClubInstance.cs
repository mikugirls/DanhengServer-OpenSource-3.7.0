namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubInstance(PlayerInstance player, uint challengeId)
{
    public PlayerInstance Player { get; } = player;
    public uint ChallengeId { get; } = challengeId;
    public int CurrentRound { get; private set; } = 0; // 当前轮次
    public List<uint> SelectedBuffs { get; } = []; // 已选共鸣
    
    // 每一局的最大轮次（搏击俱乐部通常是 4 轮或 5 轮）
    private const int MaxRounds = 4;

    /// <summary>
    /// 处理战斗结算
    /// </summary>
    public async ValueTask OnBattleEnd(PVEBattleResultCsReq req)
    {
        if (req.EndStatus == BattleEndStatus.BattleEndWin)
        {
            CurrentRound++;
            
            if (CurrentRound >= MaxRounds)
            {
                // --- 全部打完，功德圆满 ---
                await EndChallenge(true);
            }
            else
            {
                // --- 还没打完，弹出“共鸣选择”界面 ---
                // 这里调用 Manager 生成三选一的快照
                var manager = Player.GetManager<BoxingClubManager>();
                var snapshot = manager?.ProcessChooseResonance(ChallengeId, CurrentRound);
                if (snapshot != null)
                {
                    await Player.SendPacket(new PacketGetBoxingClubInfoScRsp(snapshot));
                }
            }
        }
        else
        {
            // --- 输了或撤退，直接结束挑战 ---
            await EndChallenge(false);
        }
    }

    /// <summary>
    /// 结束整个挑战
    /// </summary>
    private async ValueTask EndChallenge(bool isWin)
    {
        // 1. 核心逻辑：切回大世界阵容 (模仿忘却之庭)
        // 这一步会让 CurExtraLineup 变回 -1，回到槽位 0
        await Player.LineupManager!.SetExtraLineup(EggLink.DanhengServer.Enums.Avatar.ExtraLineupType.LineupNone);

        // 2. 清理管理器中的实例引用
        var manager = Player.GetManager<BoxingClubManager>();
        if (manager != null) manager.ChallengeInstance = null;

        Console.WriteLine($"[Boxing] 挑战结束。状态：{(isWin ? "全胜" : "中止")}。阵容已切回大世界。");
    }
}
