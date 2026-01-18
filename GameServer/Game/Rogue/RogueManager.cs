using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Rogue;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.Inventory; // 引用数据库的 ItemData
using EggLink.DanhengServer.GameServer.Server.Packet.Send.PlayerSync; // 引用同步包
namespace EggLink.DanhengServer.GameServer.Game.Rogue;

public class RogueManager(PlayerInstance player) : BasePlayerManager(player)
{
    #region Properties

    public RogueInstance? RogueInstance { get; set; }

    #endregion

    #region Information

    /// <summary>
    ///     Get the beginning time and end time
    /// </summary>
    /// <returns></returns>
    public static (long, long) GetCurrentRogueTime()
    {
        // get the first day of the week
        var beginTime = DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek).AddHours(4);
        var endTime = beginTime.AddDays(7);
        return (beginTime.ToUnixSec(), endTime.ToUnixSec());
    }
	

   // 1. 获取分数并处理周重置逻辑
   public int GetRogueScore()
{
    var time = GetCurrentRogueTime();
    long beginTime = time.Item1;
    long endTime = time.Item2;

    // 如果检测到跨周（不在本周区间内）
    if (Player.Data.LastRogueScoreUpdate < beginTime || Player.Data.LastRogueScoreUpdate > endTime)
    {
        Player.Data.RogueScore = 0; // 重置积分
        
        // --- 核心修正：同步清理 SQLite 存储的字符串字段 ---
        Player.Data.TakenRogueRewardIds = ""; 
        
        // 清理内存中的列表防止后续逻辑误判
        Player.Data.TakenRogueRewardList?.Clear(); 
        
        Player.Data.LastRogueScoreUpdate = beginTime; // 更新时间戳
        
        // 标记保存数据库，确保 TakenRogueRewardIds 的空值被写入 SQLite
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);
        return 0;
    }
    
    // 强制转换为 int 返回
    return (int)Player.Data.RogueScore; 
}

    

    public void AddRogueScore(int score)
    {
		if (score <= 0) return;

    // 增加积分并记录当前时间
    Player.Data.RogueScore += (uint)score;
    Player.Data.LastRogueScoreUpdate = Extensions.GetUnixSec();

    // 标记该玩家数据需要保存
    // PlayerInstance 里的心跳检测会自动处理后续保存逻辑
    EggLink.DanhengServer.Database.DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);
    }

    public static RogueManagerExcel? GetCurrentManager()
    {
        foreach (var manager in GameData.RogueManagerData.Values)
            if (DateTime.Now >= manager.BeginTimeDate && DateTime.Now <= manager.EndTimeDate)
                return manager;
        return null;
    }

    #endregion

    #region Actions

  // --- 【修改 StartRogue 方法】 ---
    public async ValueTask StartRogue(int areaId, int aeonId, List<int> disableAeonId, List<int> baseAvatarIds)
    {	Console.WriteLine($"[RogueDebug] 客户端请求进入: AreaId={areaId}, AeonId={aeonId}");
         if (GetRogueInstance() != null) return;
        GameData.RogueAreaConfigData.TryGetValue(areaId, out var area);
        GameData.RogueAeonData.TryGetValue(aeonId, out var aeon);

        if (area == null || aeon == null) return;

        Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupRogue, baseAvatarIds);
        await Player.LineupManager!.GainMp(8, false);
        await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager!.GetCurLineup()!));

        foreach (var id in baseAvatarIds)
        {
            Player.AvatarManager!.GetFormalAvatar(id)?.SetCurHp(10000, true);
            Player.AvatarManager!.GetFormalAvatar(id)?.SetCurSp(5000, true);
        }

        RogueInstance = new RogueInstance(area, aeon, Player);
        await RogueInstance.EnterRoom(RogueInstance.StartSiteId);

        await Player.SendPacket(new PacketSyncRogueStatusScNotify(RogueInstance.Status));
        await Player.SendPacket(new PacketStartRogueScRsp(Player));
    }
    // --- 【修改结束】 ---
	// =========================================================================
    // 【新增】通关结算：发奖励 + 解锁下一关 + 保存数据库
    // =========================================================================
    public async ValueTask FinishRogue(int currentAreaId, bool isWin)
    {
        if (!isWin) return;

        Console.WriteLine($"[RogueManager] 玩家通关 AreaId: {currentAreaId}, 开始结算...");

        // 1. 发放 100 星琼 (ItemId: 1)
        await Player.InventoryManager!.AddItem(1, 100, notify: true);
        Console.WriteLine("[RogueManager] 已发放通关奖励: 星琼 x100");

        // 2. 解锁下一关逻辑
        // 假设关卡 ID 是按 10 递增的 (110 -> 120 -> 130...)
        // 如果当前打通的关卡 >= 记录的进度，说明是推图，进度 +10
        if (currentAreaId >= Player.Data.RogueUnlockProgress)
        {
            // 比如当前是 110，打完变成 120 (解锁下一关)
            Player.Data.RogueUnlockProgress = currentAreaId + 10;
        }

        // 3. 触发数据库保存
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);
        Console.WriteLine($"[RogueManager] 进度已保存! 当前解锁至: {Player.Data.RogueUnlockProgress}");
    }		
   private static readonly Dictionary<int, int[]> WorldToRelicSets = new()
    {
        { 1, [01, 02] }, // 世界3: 空间站(01), 仙舟(02)
        { 2, [07, 08] }, // 世界4: 塔利亚(07), 翁瓦克(08)
        { 3, [03, 09] }, // 世界5: 公司(03), 泰科铵(09)
        { 4, [04, 06] }, // 世界6: 贝洛伯格(04), 停转(06)
        { 5, [10, 05] }, // 世界7: 繁星(10), 龙骨(05)
        { 6, [11, 12] }, // 世界8: 格拉默(11), 匹诺康尼(12)
        { 7, [13, 14] }, // 世界9: 茨冈尼亚(13), 出云(14)
    };

   public async ValueTask GrantImmersiveRewards()
{
    var instance = RogueInstance;
    if (instance == null) return;

    int progress = instance.AreaExcel.AreaProgress;
    int rogueDifficulty = instance.AreaExcel.Difficulty;
    int worldLevel = Player.Data.WorldLevel;

    // 决定品质档位 (3-6)
    int rank = 4; // 默认紫色
    if (rogueDifficulty >= 4) rank = 6;
    else if (rogueDifficulty == 3) rank = 5;

    if (!WorldToRelicSets.TryGetValue(progress, out var setIds))
        setIds = [01, 02];

    // 收集要发放的 ID
    List<int> itemIds = new(); // 这里用 int 列表，避免后续转换麻烦
    foreach (var setId in setIds)
    {
        itemIds.Add((rank * 10000) + 3000 + (setId * 10) + 5); // 球
        itemIds.Add((rank * 10000) + 3000 + (setId * 10) + 6); // 绳
    }

    // 难度加成
    if (rogueDifficulty >= 4 && worldLevel >= 5)
    {
        int extraSet = setIds[Random.Shared.Next(setIds.Length)];
        itemIds.Add((rank * 10000) + 3000 + (extraSet * 10) + Random.Shared.Next(5, 7));
    }

    // 执行发放
    foreach (var id in itemIds)
    {
        // 确保这里调用的是 AddItem，并且参数类型匹配
        await Player.InventoryManager!.AddItem(id, 1, notify: true);
    }
}
    public BaseRogueInstance? GetRogueInstance()
    {
        if (RogueInstance != null)
            return RogueInstance;

        if (Player.ChessRogueManager?.RogueInstance != null)
            return Player.ChessRogueManager.RogueInstance;

        if (Player.RogueMagicManager?.RogueMagicInstance != null)
            return Player.RogueMagicManager.RogueMagicInstance;

        return Player.RogueTournManager?.RogueTournInstance;
    }

    #endregion

    #region Serialization

    public RogueInfo ToProto()
    {
        var proto = new RogueInfo
        {
            RogueGetInfo = ToGetProto()
        };

        if (RogueInstance != null) proto.RogueCurrentInfo = RogueInstance.ToProto();

        return proto;
    }

    public RogueGetInfo ToGetProto()
    {
        return new RogueGetInfo
        {
            RogueScoreRewardInfo = ToRewardProto(),
            RogueAeonInfo = ToAeonInfo(),
            RogueSeasonInfo = ToSeasonProto(),
            RogueAreaInfo = ToAreaProto(),
            RogueVirtualItemInfo = ToVirtualItemProto()
        };
    }

public async ValueTask HandleTakeRogueScoreReward(TakeRogueScoreRewardCsReq req)
{
    var score = GetRogueScore();
    // 1. 从数据库字符串恢复列表
    var takenList = string.IsNullOrEmpty(Player.Data.TakenRogueRewardIds) 
        ? new List<uint>() 
        : Player.Data.TakenRogueRewardIds.Split(',').Select(uint.Parse).ToList();

    List<EggLink.DanhengServer.Database.Inventory.ItemData> syncItems = [];
    List<uint> successIds = [];
    List<(uint itemId, uint count)> displayRewards = [];
    bool isChanged = false;

    foreach (var rowId in req.LMMFPCOKHEE) 
    {
        // 2. 校验：是否已领取
        if (takenList.Contains(rowId)) continue;

        var config = GameData.RogueScoreRewardData.Values
            .FirstOrDefault(x => x.ScoreRow == (int)rowId);

        if (config != null && score >= config.Score)
        {
            // 3. 动态获取奖励 (由 RewardData.json 提供)
            if (GameData.RewardDataData.TryGetValue(config.Reward, out var rewardExcel))
            {
                foreach (var (itemId, itemCount) in rewardExcel.GetItems())
                {
                    var itemData = await Player.InventoryManager!.AddItem(itemId, itemCount, false, sync: false, returnRaw: true);
                    if (itemData != null) 
                    {
                        syncItems.Add(itemData);
                        displayRewards.Add(((uint)itemId, (uint)itemCount));
                    }
                }
                
                successIds.Add(rowId);
                takenList.Add(rowId); // 记录到临时列表
                isChanged = true;
            }
        }
    }

    if (isChanged)
    {
        // 4. 将列表序列化回字符串存入 Player.Data
        Player.Data.TakenRogueRewardIds = string.Join(",", takenList);
        // 5. 触发 SQLite 异步保存
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid); 
    }

    if (syncItems.Count > 0) await Player.SendPacket(new PacketPlayerSyncScNotify(syncItems));
    await Player.SendPacket(new PacketTakeRogueScoreRewardScRsp(Player, successIds, displayRewards));
}

   public RogueScoreRewardInfo ToRewardProto()
{
    var time = GetCurrentRogueTime();

    var proto = new RogueScoreRewardInfo
    {
        ExploreScore = (uint)GetRogueScore(),
        PoolRefreshed = true,
        PoolId = (uint)(20 + Player.Data.WorldLevel),
        RewardBeginTime = time.Item1,
        RewardEndTime = time.Item2,
        HasTakenInitialScore = true
    };
// 从数据库字符串同步给协议列表
    if (!string.IsNullOrEmpty(Player.Data.TakenRogueRewardIds))
    {
        var ids = Player.Data.TakenRogueRewardIds.Split(',').Select(uint.Parse);
        proto.TakenNormalFreeRowList.AddRange(ids);
    }

    return proto;
}

    public static RogueAeonInfo ToAeonInfo()
    {
        var proto = new RogueAeonInfo
        {
            IsUnlocked = true,
            UnlockedAeonNum = (uint)GameData.RogueAeonData.Count,
            UnlockedAeonEnhanceNum = 3
        };

        proto.AeonIdList.AddRange(GameData.RogueAeonData.Keys.Select(x => (uint)x));

        return proto;
    }

    public static RogueSeasonInfo ToSeasonProto()
    {
        var manager = GetCurrentManager();
        if (manager == null) return new RogueSeasonInfo();

        return new RogueSeasonInfo
        {
            Season = (uint)manager.RogueSeason,
            BeginTime = manager.BeginTimeDate.ToUnixSec(),
            EndTime = manager.EndTimeDate.ToUnixSec()
        };
    }

   // 【修改】去掉 static，让它读取玩家进度
    public RogueAreaInfo ToAreaProto()
    {
        var manager = GetCurrentManager();
        if (manager == null) return new RogueAreaInfo();

        // 获取当前进度 (我们在 PlayerData 里初始设为了 110)
        int progress = Player.Data.RogueUnlockProgress;

        return new RogueAreaInfo
        {
            RogueAreaList =
            {
                manager.RogueAreaIDList.Select(x => new RogueArea
                {
                    AreaId = (uint)x,
                    // 【关键】如果 关卡ID <= 进度，状态设为 FirstPass (已解锁)
                    // 否则设为 Close (未解锁)
                  // 【核心修复】
                    // 1. 如果 x < progress: 说明这一关已经打过去了 -> FirstPass (已通关)
                    // 2. 如果 x == progress: 说明刚好解锁到这一关 -> Unlock (已解锁)
                    // 3. 如果 x > progress: 说明是后面的关卡 -> Lock (锁定)
					AreaStatus = x <= progress ? RogueAreaStatus.Unlock : RogueAreaStatus.Lock,
                    
                    // 【关键】设为 false，表示“首通奖励还没领”
                    // 以后你写了奖励系统，把这里改成读取数据库状态即可
                    HasTakenReward = false
                })
            }
        };
    }

    public RogueGetVirtualItemInfo ToVirtualItemProto()
    {
       
            return new RogueGetVirtualItemInfo
    {
        DKABGHHOODP = (uint)(RogueInstance?.CurMoney ?? 0), // 沉浸券
        TalentPoint = (uint)Player.Data.TalentPoints,      // 技能点
        
        // BILEOOPHJEF 可能是某种特殊的活动积分，暂时设为 0
        // 核心修正：对应结算界面中的沉浸器显示
        BILEOOPHJEF = (uint)Player.Data.ImmersiveArtifact
    };
        
    }

    public static RogueTalentInfoList ToTalentProto()
    {
        var proto = new RogueTalentInfoList();

        foreach (var talent in GameData.RogueTalentData)
            proto.TalentInfo.Add(new RogueTalentInfo
            {
                TalentId = (uint)talent.Key,
                Status = RogueTalentStatus.Enable
            });

        return proto;
    }

    #endregion
}
