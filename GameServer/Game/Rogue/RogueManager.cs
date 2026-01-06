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

    public async ValueTask StartRogue(int areaId, int aeonId, List<int> disableAeonId, List<int> baseAvatarIds)
    {
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

    public static RogueAreaInfo ToAreaProto()
    {
        var manager = GetCurrentManager();
        if (manager == null) return new RogueAreaInfo();
        return new RogueAreaInfo
        {
            RogueAreaList =
            {
                manager.RogueAreaIDList.Select(x => new RogueArea
                {
                    AreaId = (uint)x,
                    AreaStatus = RogueAreaStatus.FirstPass,
                    HasTakenReward = true
                })
            }
        };
    }

    public static RogueGetVirtualItemInfo ToVirtualItemProto()
    {
        return new RogueGetVirtualItemInfo
        {
            // TODO: Implement
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