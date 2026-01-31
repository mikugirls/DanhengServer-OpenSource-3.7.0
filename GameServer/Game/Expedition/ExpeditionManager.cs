using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Database.Expedition;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

namespace EggLink.DanhengServer.GameServer.Game.Expedition;

public class ExpeditionManager : BasePlayerManager
{
    // 直接引用 PlayerInstance 中初始化好的 Data
    public ExpeditionData Data => Player.ExpeditionData!;
	// 1. 请确保在类开头定义了此常量
	private const int EXPEDITION_REWARD_MULTIPLIER = 10;
    public ExpeditionManager(PlayerInstance player) : base(player) { }

    public async ValueTask Initialize()
    {
        // 玩家上线时同步一次全量数据
        await SyncExpeditionData();
    }

    #region Main Actions

    /// <summary>
    /// 处理开始派遣请求 (CmdId: 2521)
    /// </summary>
   /// <summary>
/// 处理开始派遣请求 (CmdId: 2521)
/// </summary>
/// <summary>
    /// 处理开始派遣请求 (CmdId: 2521)
    /// </summary>
    public async ValueTask AcceptExpedition(AcceptExpeditionCsReq req)
    {
        var info = req.AcceptExpedition;
        if (info == null) 
        {
            Console.WriteLine("[派遣开始] 错误：收到空请求体 (AcceptExpedition == null)");
            return;
        }

        Console.WriteLine($"[派遣开始] 收到请求 - 点位ID: {info.Id}, 请求时长: {info.TotalDuration}s (约为 {info.TotalDuration / 3600.0:F1}h)");

        // 1. 获取派遣静态配置 
        if (!GameData.ExpeditionDataData.TryGetValue((int)info.Id, out var config))
        {
            Console.WriteLine($"[派遣开始] 失败：配置表中找不到点位 ID {(int)info.Id}");
            return;
        }

        // 2. 校验队伍上限
        int unlockedSlots = GetUnlockedExpeditionSlots();
        if (Data.ExpeditionList.Count >= unlockedSlots)
        {
            Console.WriteLine($"[派遣开始] 拦截：槽位已满 ({Data.ExpeditionList.Count}/{unlockedSlots})");
            return;
        }

        // 3. 校验解锁任务条件 
        if (config.UnlockMission > 0 && 
            Player.MissionManager!.GetMainMissionStatus(config.UnlockMission) != Enums.Mission.MissionPhaseEnum.Finish)
        {
            Console.WriteLine($"[派遣开始] 拦截：前置任务 {config.UnlockMission} 未完成");
            return;
        }

        // 4. 校验人数限制
        if (info.AvatarIdList.Count < config.AvatarNumMin || info.AvatarIdList.Count > config.AvatarNumMax)
        {
            Console.WriteLine($"[派遣开始] 拦截：人数不符。需 {config.AvatarNumMin}-{config.AvatarNumMax} 人，实际发送 {info.AvatarIdList.Count} 人");
            return;
        }

        // 5. 校验派遣时长与奖励配置
        // 关键点：检查时长匹配
        var rewardConfig = GameData.ExpeditionIdToRewards[(int)info.Id]
		.FirstOrDefault(x => (uint)x.Duration == info.TotalDuration);
        
        if (rewardConfig == null) 
		{
		var availableDurations = string.Join(", ", GameData.ExpeditionIdToRewards[(int)info.Id].Select(x => x.Duration + "h"));
		Console.WriteLine($"[派遣开始] 拦截：时长匹配失败。客户端传值 {info.TotalDuration}，配置表支持: {availableDurations}");
		return;
		}

        // 6. 校验角色是否被重复占用
        foreach (var avatarId in info.AvatarIdList)
        {
            if (Data.ExpeditionList.Any(ex => ex.AvatarIdList.Contains(avatarId)))
            {
                Console.WriteLine($"[派遣开始] 拦截：角色 {avatarId} 已在其他派遣任务中");
                return;
            }
        }

        // 7. 保存至数据库 
        var newExpedition = new ExpeditionInfoInstance
        {
            Id = info.Id,
            TotalDuration = info.TotalDuration,
            StartExpeditionTime = Extensions.GetUnixSec(), 
            AvatarIdList = info.AvatarIdList.ToList()
        };
        
        Data.ExpeditionList.Add(newExpedition);
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);
        Console.WriteLine($"[派遣开始] 成功！点位 {info.Id} 已存入，列表总数: {Data.ExpeditionList.Count}");

        // 8. 发送回执
        await Player.SendPacket(new PacketAcceptExpeditionScRsp(newExpedition));

        // 9. 全量同步
        await SyncExpeditionData();
    }

    /// <summary>
    /// 领取派遣奖励 (CmdId: 2552) - 完整双重加成版
    /// </summary>
   /// <summary>
/// 领取派遣奖励 (CmdId: 2552) - 10倍奖励与物理时间跳变修正版
/// </summary>
public async ValueTask TakeExpeditionReward(uint expeditionId)
{
    // 1. 查找派遣实例
    var instance = Data.ExpeditionList.FirstOrDefault(x => x.Id == expeditionId);
    if (instance == null) return;

    // 2. 获取点位配置
    if (!GameData.ExpeditionDataData.TryGetValue((int)instance.Id, out var config))
        return;

    // 3. 获取奖励配置
    var rewardConfig = GameData.ExpeditionIdToRewards[(int)instance.Id]
        .FirstOrDefault(x => (uint)x.Duration == instance.TotalDuration);

    // --- 核心校验：绝对时间差判定 ---
    // 使用 Math.Abs 确保无论是 RunAsDate 往后拨(加速)还是往前拨(跳变)，只要跨度够大即可领奖
    long timeElapsed = Math.Abs(Extensions.GetUnixSec() - instance.StartExpeditionTime);
    long requiredTime = (long)instance.TotalDuration * 3600;

    if (timeElapsed < requiredTime)
    {
        long remaining = requiredTime - timeElapsed;
        Console.WriteLine($"[派遣领奖] 时间跨度不足！当前跨度 {timeElapsed}秒，还需 {remaining}秒。");
        return;
    }
    
    if (rewardConfig != null)
    {
        // --- 核心加成判定：命途 + 属性 ---
        bool hasBonus = false;
        foreach (var avatarId in instance.AvatarIdList)
        {
            if (GameData.AvatarConfigData.TryGetValue((int)avatarId, out var avatarExcel))
            {
                // A. 判定命途加成
                if (config.BonusBaseTypeList.Count > 0 && 
                    config.BonusBaseTypeList.Contains(avatarExcel.AvatarBaseType.ToString()))
                {
                    hasBonus = true;
                    break; 
                }

                // B. 判定属性加成
                if (config.BonusDamageTypeList.Count > 0 && 
                    config.BonusDamageTypeList.Contains(avatarExcel.DamageType.ToString()))
                {
                    hasBonus = true;
                    break;
                }
            }
        }

        // 4. 发放基础奖励 (传入 10 倍常量)
        // 注意：这里会调用我们修复后的 HandleReward(int, bool, bool, int)
        var rewardItems = await Player.InventoryManager!.HandleReward(
            rewardConfig.RewardID, 
            notify: true, 
            sync: true, 
            multiplier: EXPEDITION_REWARD_MULTIPLIER);

        var rewardProto = new ItemList();
        rewardProto.ItemList_.AddRange(rewardItems.Select(x => x.ToProto()));

        // 5. 发放额外奖励 (同样应用 10 倍常量)
        var extraRewardProto = new ItemList();
        if (hasBonus && rewardConfig.ExtraRewardID > 0)
        {
            var extraItems = await Player.InventoryManager!.HandleReward(
                rewardConfig.ExtraRewardID, 
                notify: true, 
                sync: true, 
                multiplier: EXPEDITION_REWARD_MULTIPLIER);
            
            extraRewardProto.ItemList_.AddRange(extraItems.Select(x => x.ToProto()));
        }

        // 6. 发送领奖回执
        await Player.SendPacket(new PacketTakeExpeditionRewardScRsp(expeditionId, rewardProto, extraRewardProto));
    }

    // 7. 清理数据并持久化
    Data.ExpeditionList.Remove(instance);
    Data.TotalFinishedCount++; 
    DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);

    // 8. 同步最新状态（刷新格子）
    await SyncExpeditionData();

    Console.WriteLine($"[派遣领奖] 成功！点位: {expeditionId}, 物理耗时: {timeElapsed}s, 奖励倍率: {EXPEDITION_REWARD_MULTIPLIER}x");
}

    /// <summary>
    /// 中途取消派遣 (CmdId: 2584)
    /// </summary>
    public async ValueTask CancelExpedition(uint expeditionId)
    {
        var instance = Data.ExpeditionList.FirstOrDefault(x => x.Id == expeditionId);
        if (instance == null) return;

        Data.ExpeditionList.Remove(instance);
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);

        await Player.SendPacket(new PacketCancelExpeditionScRsp(expeditionId));
        
        // 同步释放槽位
        await SyncExpeditionData();
    }

    #endregion

    #region Helpers

    public async ValueTask SyncExpeditionData()
    {
        // 1. 获取已解锁的点位 ID 列表
        var unlockedIds = GameData.ExpeditionDataData.Values
            .Where(config => config.UnlockMission == 0 || 
                   Player.MissionManager!.GetMainMissionStatus(config.UnlockMission) == Enums.Mission.MissionPhaseEnum.Finish)
            .Select(config => (uint)config.ExpeditionID)
            .ToList();

        // 2. 构造通知包 (对齐 3.7.0 Proto 字段)
        var proto = new ExpeditionDataChangeScNotify
        {
            TotalExpeditionCount = (uint)Data.TotalFinishedCount, 
            ExpeditionInfo = { Data.ExpeditionList.Select(x => x.ToProto()) },
            JFJPADLALMD = { unlockedIds } // 对应 UnlockedExpeditionIdList 的混淆名
        };

        await Player.SendPacket(new PacketExpeditionDataChangeScNotify(proto));
    }

    public int GetUnlockedExpeditionSlots()
    {
        int count = 0;
        foreach (var team in GameData.ExpeditionTeamData.Values)
        {
            if (team.UnlockMission == 0 || 
                Player.MissionManager!.GetMainMissionStatus(team.UnlockMission) == Enums.Mission.MissionPhaseEnum.Finish)
            {
                count++;
            }
        }
        return count == 0 ? 2 : count;
    }

    #endregion
}
