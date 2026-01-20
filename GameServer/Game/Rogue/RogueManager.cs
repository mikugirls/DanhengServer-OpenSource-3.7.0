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
    private HashSet<int> GetClearedAreaIds()
    {
        // 读取 PlayerData 里的新字段
        if (string.IsNullOrEmpty(Player.Data.RogueFinishedAreaIds)) 
            return new HashSet<int>();
            
        return Player.Data.RogueFinishedAreaIds
            .Split(',')
            .Where(s => !string.IsNullOrEmpty(s)) // 防止空字符报错
            .Select(int.Parse)
            .ToHashSet();
    }

    // =========================================================================
    // 【新增辅助方法 2】保存通关记录
    // 作用：通关后把 ID 加进去，转成字符串存回数据库
    // =========================================================================
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
        
        // 1. 如果没打赢，直接返回空
        if (!isWin) return totalRewards;

        // 获取已通关列表
        var clearedSet = GetClearedAreaIds();

        // 2. 检查首通状态
        // 如果列表里已经有这个 ID，说明以前通过关，领过首通奖了 -> 跳过奖励
        if (clearedSet.Contains(currentAreaId))
        {
            Console.WriteLine($"[RogueManager] AreaId: {currentAreaId} 已在通关记录中，本次无首通奖励。");
            return totalRewards; 
        }

        Console.WriteLine($"[RogueManager] 首次通关 AreaId: {currentAreaId}，准备发放首通奖励...");

        if (GameData.RogueAreaConfigData.TryGetValue(currentAreaId, out var areaConfig))
        {
            // 获取难度 (防错处理)
            int difficulty = areaConfig.Difficulty; 
            if (difficulty == 0) difficulty = 1;

            // --- 奖励分流逻辑 (这部分和你之前的一样) ---
            if (currentAreaId < 130)
            {
                // [世界1、2] 读取 Excel 固定奖励
                int firstRewardId = areaConfig.FirstReward;
                if (firstRewardId > 0)
                {
                    var rewards = await Player.InventoryManager!.HandleReward(firstRewardId, notify: true, sync: true);
                    if (rewards != null) totalRewards.AddRange(rewards);
                }
            }
            else
            {
                // [世界3及以后] 权重随机奖励
                int worldIndex = (currentAreaId - 100) / 10; 
                var relicRewards = GenerateWorldRelicRewards(worldIndex, difficulty);
                
                foreach (var item in relicRewards)
                {
                    // 注意这里用了 (int) 强转防止报错
                    var reward = await Player.InventoryManager!.AddItem(item.ItemId, (int)item.Count, notify: true, sync: true);
                    
                    if (reward != null) totalRewards.Add(reward);
                }
                Console.WriteLine($"[Rogue] 世界{worldIndex} (难度{difficulty}) 权重奖励发放完成，共 {totalRewards.Count} 个物品");
            }

            // 3. 【核心修改】保存进度
            // 发完奖后，把这个 ID 存入数据库，下次就不发了
            SaveClearedAreaId(currentAreaId);
        }

        await System.Threading.Tasks.Task.CompletedTask;
        return totalRewards; 
    }
    // 1. 主生成逻辑
    private List<ItemData> GenerateWorldRelicRewards(int worldIndex, int difficulty)
    {
        List<ItemData> list = new();

        // A. 决定掉落数量 (权重随机：2个 or 3个)
        int count = GetWeightedDropCount(difficulty);

        // B. 获取该世界的套装列表
        int[] setIds = GetWorldRelicSets(worldIndex);

        // C. 循环生成
        for (int i = 0; i < count; i++)
        {
            // 决定品质 (权重随机：蓝/紫/金)
            int rank = GetWeightedRarity(difficulty);

            // 63xxx(蓝), 62xxx(紫), 61xxx(金)
            int baseIdPrefix = rank switch { 5 => 61000, 4 => 62000, _ => 63000 };

            // 随机套装 + 随机部位
            int setId = setIds[Random.Shared.Next(setIds.Length)];
            int part = Random.Shared.Next(5, 7); // 5=球, 6=绳
            
            int relicId = baseIdPrefix + (setId * 10) + part;
            list.Add(new ItemData { ItemId = relicId, Count = 1 });
        }

        // D. 随机附赠遗器经验 (数量随难度波动)
        int expCount = difficulty * Random.Shared.Next(2, 5); 
        list.Add(new ItemData { ItemId = 235, Count = (int)expCount });

        return list;
    }

    // 2. 计算掉落数量权重
    private int GetWeightedDropCount(int difficulty)
    {
        int roll = Random.Shared.Next(0, 100);
        if (difficulty >= 5) return roll < 80 ? 3 : 2; // 难度5+: 80%几率掉3个
        if (difficulty >= 3) return roll < 30 ? 3 : 2; // 难度3-4: 30%几率掉3个
        return 2; // 低难度固定2个
    }

    // 3. 计算品质权重 (核心概率表)
    private int GetWeightedRarity(int difficulty)
    {
        int roll = Random.Shared.Next(0, 100);
        switch (difficulty)
        {
            case 1: // 难度1: 20%紫, 80%蓝
                return roll < 20 ? 4 : 3;
            case 2: // 难度2: 5%金, 55%紫, 40%蓝
                if (roll < 5) return 5;
                if (roll < 60) return 4;
                return 3;
            case 3: // 难度3: 30%金, 70%紫
                return roll < 30 ? 5 : 4;
            case 4: // 难度4: 60%金, 40%紫
                return roll < 60 ? 5 : 4;
            default: // 难度5+: 85%金, 15%紫
                return roll < 85 ? 5 : 4;
        }
    }

    // 4. 获取世界套装映射
    private int[] GetWorldRelicSets(int worldIndex)
    {
        return worldIndex switch
        {
            3 => [305, 309], // 太空, 仙舟
            4 => [306, 308], // 盗贼, 翁瓦克
            5 => [307, 310], // 公司, 星体
            6 => [311, 312], // 萨尔索图, 贝洛伯格
            7 => [313, 314], // 繁星, 龙骨
            8 => [315, 316], // 苍穹, 匹诺康尼
            9 => [317, 318], // 出云, 荒星
            _ => [305, 309]
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
    // =========================================================================
    // 【修改后的 ToAreaProto】
    // 逻辑：发送关卡列表给客户端，告诉它哪些解锁了，哪些领过奖了
    // =========================================================================
    public RogueAreaInfo ToAreaProto()
    {
        var manager = GetCurrentManager();
        if (manager == null) return new RogueAreaInfo();

        // 1. 获取已通关 ID 列表 (例如: {110, 120, 130})
        var clearedSet = GetClearedAreaIds();

        // 2. 准备工作：建立一张表，记录 "第几世界 -> 这个世界难度1的ID"
        //    例如: {1: 110, 2: 120, 3: 130...}
        //    这用于判断跨世界解锁 (比如打通世界2，解锁世界3)
        var worldBaseIds = GameData.RogueAreaConfigData.Values
            .Where(x => x.Difficulty == 1) // 只看难度1
            .GroupBy(x => x.AreaProgress)  // 按进度分组
            .ToDictionary(g => g.Key, g => g.First().RogueAreaID);

        return new RogueAreaInfo
        {
            RogueAreaList =
            {
                manager.RogueAreaIDList.Select(areaId => 
                {
                    // 容错：如果 Excel 里没这关，直接锁住
                    if (!GameData.RogueAreaConfigData.TryGetValue(areaId, out var areaConfig))
                        return new RogueArea { AreaId = (uint)areaId, AreaStatus = RogueAreaStatus.Lock };

                    var status = RogueAreaStatus.Lock;

                    // --- 【核心解锁判定】 ---
                    
                    // 情况 A: 这一关我已经打通了 -> 肯定是解锁的
                    if (clearedSet.Contains(areaId))
                    {
                        status = RogueAreaStatus.Unlock;
                    }
                    else
                    {
                        // 情况 B: 没打通，检查前置条件是否满足
                        int preAreaId = -1;

                        if (areaConfig.Difficulty > 1)
                        {
                            // 分支 1：高难度关卡 (例如 131, 难度2)
                            // 前置条件：必须通关同一个世界的上一难度 (即 130)
                            // 规律：ID 减 1
                            preAreaId = areaId - 1;
                        }
                        else
                        {
                            // 分支 2：新世界入口 (例如 130, 世界3难度1)
                            // 前置条件：必须通关上一个世界的难度 1
                            // 逻辑：当前进度(3) - 1 = 2，去表里查进度2对应的 ID (120)
                            int preProgress = areaConfig.AreaProgress - 1;
                            
                            if (worldBaseIds.TryGetValue(preProgress, out int prevWorldId))
                            {
                                preAreaId = prevWorldId; // 找到了前置世界ID
                            }
                            else if (preProgress < 1) 
                            {
                                // 如果没有前置进度 (比如世界1的前面是0)，说明是第一关 -> 默认解锁
                                status = RogueAreaStatus.Unlock;
                            }
                        }

                        // 如果计算出了前置 ID，且前置 ID 已经打通了 -> 解锁当前关
                        if (status == RogueAreaStatus.Lock && preAreaId != -1)
                        {
                            if (clearedSet.Contains(preAreaId))
                            {
                                status = RogueAreaStatus.Unlock;
                            }
                        }
                    }

                    // --- 【奖励领取判定】 ---
                    // 列表里有 ID = 已通关 = 已领奖
                    // 客户端会根据这个显示那个“首通奖励”的小勾勾
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
