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
  // =========================================================================
    // 【新增辅助方法 1】读取已通关列表
    // 作用：把数据库里的 "110,120,130" 字符串变成集合，方便快速检查
    // =========================================================================
   // =========================================================================
    // 【修改后的 GetClearedAreaIds】(带自动初始化功能)
    // 逻辑：读取列表 -> 如果发现缺了 100/110 -> 补全并存库 -> 返回列表
    // =========================================================================
   
    // =========================================================================
    // 【新增辅助方法 2】保存通关记录
    // 作用：通关后把 ID 加进去，转成字符串存回数据库
    // =========================================================================
    // =========================================================================
    // 【修改后的 GetClearedAreaIds】(纯净读取版)
    // 逻辑：只读取数据库，不要强制塞 110 进去，否则会导致开局就算已领奖
    // =========================================================================
    private HashSet<int> GetClearedAreaIds()
    {
        if (string.IsNullOrEmpty(Player.Data.RogueFinishedAreaIds)) 
            return new HashSet<int>();
            
        return Player.Data.RogueFinishedAreaIds
            .Split(',')
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(int.Parse)
            .ToHashSet();
    }
	private void SaveClearedAreaId(int areaId)
    {
        var clearedSet = GetClearedAreaIds();
        
        // 如果已经存过了，就不用再存了
        if (clearedSet.Contains(areaId)) return; 

        // 加入集合
        clearedSet.Add(areaId);
        
        // 转回字符串存入 Player.Data
        Player.Data.RogueFinishedAreaIds = string.Join(",", clearedSet);
        
        // 标记保存到数据库 (这一步很重要，否则重启服务器记录就丢了)
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);
    }
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
   
    

    // =========================================================================
    // 【新增辅助方法】下面全是新加的，建议放在 FinishRogue 下面
    // =========================================================================
    // =========================================================================
    // 【修改后的 FinishRogue】
    // 逻辑：通关结算 -> 检查是否首通 -> 发奖 -> 记录通关ID
    // =========================================================================
 public async ValueTask<List<ItemData>> FinishRogue(int currentAreaId, bool isWin)
{
    List<ItemData> totalRewards = new();
    
    // 1. 基础校验
    if (!isWin) return totalRewards;
    if (!GameData.RogueAreaConfigData.TryGetValue(currentAreaId, out var areaConfig)) return totalRewards;

    int difficulty = areaConfig.Difficulty > 0 ? areaConfig.Difficulty : 1;
    int worldIndex = (currentAreaId - 100) / 10;

    // --- 2. 常规奖励：手动生成遗器 (只要打通世界 3 及以上就发) ---
    if (currentAreaId >= 130)
    {
        var relicRewards = GenerateWorldRelicRewards(worldIndex, difficulty);
        foreach (var item in relicRewards)
        {
            // 【修复关键】：只传 3 个参数 (itemId, count, notify)
            // 这样能匹配大多数版本的 AddItem 定义，避免 bool 到 int 的转换错误
            var reward = await Player.InventoryManager!.AddItem(item.ItemId, (int)item.Count, true);
            
            if (reward != null) 
            {
                // 必须 Add 进 totalRewards，结算大框才会显示图标
                totalRewards.Add(reward); 
            }
        }
        Console.WriteLine($"[Rogue-Drop] 世界{worldIndex} 常规掉落已发放。");
    }

    // --- 3. 首通奖励：统一用 HandleReward 发放 (只领一次) ---
    var clearedSet = GetClearedAreaIds();
    if (!clearedSet.Contains(currentAreaId))
    {
        Console.WriteLine($"[RogueManager] 首次通关 AreaId: {currentAreaId}，发放首通奖励...");

        int firstRewardId = areaConfig.FirstReward;
        if (firstRewardId > 0)
        {
            // HandleReward 内部会处理克隆并返回 UI 显示用的增量列表
            var firsts = await Player.InventoryManager!.HandleReward(firstRewardId, notify: true, sync: true);
            if (firsts != null) 
            {
                totalRewards.AddRange(firsts);
            }
        }

        // 保存首通记录
        SaveClearedAreaId(currentAreaId);
    }
    else
    {
        Console.WriteLine($"[RogueManager] 非首次通关，仅发放常规掉落。");
    }

    return totalRewards;
}
private List<ItemData> GenerateWorldRelicRewards(int worldIndex, int difficulty)
{
    List<ItemData> list = new();
    
    // --- 1. 确定掉落总件数 (根据世界和难度分档) ---
    int minCount, maxCount;
    if (worldIndex >= 7) { // 世界 7, 8, 9
        minCount = difficulty switch { 1 => 6, 2 => 7, 3 => 8, _ => 10 };
        maxCount = difficulty switch { 1 => 7, 2 => 8, 3 => 10, _ => 11 };
    }
    else if (worldIndex >= 5) { // 世界 5, 6
        minCount = difficulty switch { 1 => 5, 2 => 6, 3 => 7, _ => 8 };
        maxCount = difficulty switch { 1 => 6, 2 => 7, 3 => 8, _ => 9 };
    }
    else { // 世界 3, 4
        minCount = difficulty switch { 1 => 4, 2 => 5, 3 => 6, 4 => 7, _ => 8 };
        maxCount = difficulty switch { 1 => 6, 2 => 6, 3 => 7, 4 => 8, _ => 9 };
    }

    int totalCount = Random.Shared.Next(minCount, maxCount + 1);
    int[] setIds = GetWorldRelicSets(worldIndex);

    // --- 2. 确定金装(5星)保底数量 ---
    int goldCount = 0;
    if (difficulty >= 3) 
    {
        // 难度 3 及以上必定出金，设定保底区间
        if (worldIndex >= 7) goldCount = difficulty == 3 ? Random.Shared.Next(5, 8) : Random.Shared.Next(7, 10);
        else if (worldIndex >= 5) goldCount = difficulty == 3 ? Random.Shared.Next(2, 5) : Random.Shared.Next(4, 7);
        else goldCount = difficulty switch { 3 => Random.Shared.Next(1, 3), 4 => Random.Shared.Next(2, 4), _ => Random.Shared.Next(3, 6) };
        
        // 修正：保底数不能超过总件数
        goldCount = Math.Min(goldCount, totalCount);
    }

    // --- 3. 循环生成每一件掉落 (纯随机) ---
    for (int i = 0; i < totalCount; i++)
    {
        int rank;
        if (difficulty >= 3)
        {
            // 难度 3 以后：先发完保底的金，剩下的全给紫
            rank = (i < goldCount) ? 5 : 4;
        }
        else
        {
            // 难度 1-2：按概率判定
            int goldProb = difficulty == 1 ? (worldIndex >= 7 ? 35 : (worldIndex >= 5 ? 25 : 20)) 
                                           : (worldIndex >= 7 ? 55 : (worldIndex >= 5 ? 45 : 40));
            
            int roll = Random.Shared.Next(0, 100);
            if (roll < goldProb) rank = 5;
            else rank = Random.Shared.Next(0, 100) < 50 ? 4 : 3; // 剩下的蓝紫随机
        }

        // 品质前缀
        int rankPrefix = rank switch { 5 => 6, 4 => 5, _ => 4 };
        
        // 【纯随机】独立随机套装和部位
        int setId = setIds[Random.Shared.Next(setIds.Length)];
        int part = Random.Shared.Next(5, 7); 

        int relicId = (rankPrefix * 10000) + (setId * 10) + part;
        list.Add(new ItemData { ItemId = relicId, Count = 1 });
    }

    // 附赠经验素材
    list.Add(new ItemData { ItemId = 233, Count = difficulty * 2 });
    return list;
}

// 数量和概率方法已整合进主生成逻辑，无需再单独调用 GetWeightedDropCount 和 GetWeightedRarity

  private int[] GetWorldRelicSets(int worldIndex)
{
    return worldIndex switch
    {
        3 => [301, 302], // 太空封印站, 不老者的仙舟
        4 => [307, 308], // 盗贼公国, 翁瓦克
        5 => [303, 305], // 泛银河公司, 星体差分机
        6 => [304, 306], // 筑城者, 停转的萨尔索图
        7 => [309, 310], // 繁星竞技场, 折断的龙骨
        8 => [311, 312], // 苍穹战线, 梦想之地
        9 => [313, 314], // 无主荒星, 出云显世
        _ => [301, 302]  // 默认给世界3
    };
}
	// --- 【新增 UpdateRogueProgress 方法】 ---
	public async ValueTask UpdateRogueProgress(int currentAreaId)
    {
        Console.WriteLine($"[RogueManager] 玩家手动退出，正在检查通关记录...");

        // 直接调用保存逻辑，内部会自动判断是否重复，不会覆盖旧数据
        SaveClearedAreaId(currentAreaId);
        
        await System.Threading.Tasks.Task.CompletedTask;
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
  // 获取已解锁命途的集合
private HashSet<int> GetUnlockedAeonIds()
{
    if (string.IsNullOrWhiteSpace(Player.Data.UnlockedAeonIds))
        return new HashSet<int> { 1 }; // 兜底：至少有存护

    return Player.Data.UnlockedAeonIds
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => int.TryParse(s, out var id) ? id : -1)
        .Where(id => id != -1)
        .ToHashSet();
}

// 保存新解锁的命途
private void SaveUnlockedAeon(int aeonId)
{
    var unlockedSet = GetUnlockedAeonIds();
    if (unlockedSet.Add(aeonId)) // 如果是新 ID，Add 会返回 true
    {
        Player.Data.UnlockedAeonIds = string.Join(",", unlockedSet);
        // 标记：通知 DatabaseHelper 进行 5 分钟一次的物理保存
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);
    }
}
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
    {   var unlockedIds = GetUnlockedAeonIds(); // 获取玩家自己的解锁数据
        var proto = new RogueAeonInfo
        {
            IsUnlocked = true,
            UnlockedAeonNum = (uint)unlockedIds.Count,
            UnlockedAeonEnhanceNum = 3
        };
		// 遍历所有配置，只下发玩家已解锁的
    	foreach (var id in unlockedIds)
    	{
        if (GameData.RogueAeonData.ContainsKey(id))
        {
            proto.AeonIdList.Add((uint)id);
        }
    	}
       

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
    // =========================================================================
    // 【修改后的 ToAreaProto】
    // 逻辑：发送关卡列表给客户端，告诉它哪些解锁了，哪些领过奖了
    // =========================================================================
    // =========================================================================
    // 【修改后的 ToAreaProto】(完美逻辑版)
    // =========================================================================
    public RogueAreaInfo ToAreaProto()
    {
        var manager = GetCurrentManager();
        if (manager == null) return new RogueAreaInfo();

        // 1. 获取已真正打通的 ID 列表
        // 注意：刚开始玩时，这里面是空的
        var clearedSet = GetClearedAreaIds();

        // 2. 预处理前置世界映射
        var worldBaseIds = GameData.RogueAreaConfigData.Values
            .Where(x => x.Difficulty == 1)
            .GroupBy(x => x.AreaProgress)
            .ToDictionary(g => g.Key, g => g.First().RogueAreaID);

        return new RogueAreaInfo
        {
            RogueAreaList =
            {
                manager.RogueAreaIDList.Select(areaId => 
                {
                    if (!GameData.RogueAreaConfigData.TryGetValue(areaId, out var areaConfig))
                        return new RogueArea { AreaId = (uint)areaId, AreaStatus = RogueAreaStatus.Lock };

                    var status = RogueAreaStatus.Lock;

                    // --- 【核心解锁判定】 ---
                    
                    // 1. 特殊名单：100 (教学) 和 110 (第一世界) 默认永远解锁
                    //    无论是否打通过，都可以进
                    if (areaId == 100 || areaId == 110)
                    {
                        status = RogueAreaStatus.Unlock;
                    }
                    // 2. 如果已经打通了，那肯定解锁
                    else if (clearedSet.Contains(areaId))
                    {
                        status = RogueAreaStatus.Unlock;
                    }
                    // 3. 其他关卡：检查前置是否已打通
                    else
                    {
                        int preAreaId = -1;

                        if (areaConfig.Difficulty > 1)
                        {
                            preAreaId = areaId - 1; // 高难度查上一难度
                        }
                        else
                        {
                            // 新世界查上一进度
                            int preProgress = areaConfig.AreaProgress - 1;
                            if (worldBaseIds.TryGetValue(preProgress, out int prevWorldId))
                            {
                                preAreaId = prevWorldId;
                            }
                        }

                        // 只有当前置关卡【真的打赢了】(在 clearedSet 里)，才解锁当前关
                        // 这样就保证了必须打赢 110 才能解锁 120
                        if (preAreaId != -1 && clearedSet.Contains(preAreaId))
                        {
                            status = RogueAreaStatus.Unlock;
                        }
                    }

                    // --- 【奖励领取判定】 ---
                    // 这里的逻辑保持不变：只有在数据库列表里的，才算领过奖
                    // 刚开始玩时 110 不在列表里 -> HasTakenReward = false (没领)
                    // 打完一次后 110 进列表 -> HasTakenReward = true (已领)
                    bool hasTaken = clearedSet.Contains(areaId);

                    return new RogueArea
                    {
                        AreaId = (uint)areaId,
                        AreaStatus = status,
                        HasTakenReward = hasTaken
                    };
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
