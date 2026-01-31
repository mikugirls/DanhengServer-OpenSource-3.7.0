using System.IO;
using Newtonsoft.Json;
using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.Activity;
using EggLink.DanhengServer.GameServer.Game.Activity.Activities;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Activity; 
using EggLink.DanhengServer.Database.Inventory;
namespace EggLink.DanhengServer.GameServer.Game.Activity;

public class ActivityManager : BasePlayerManager
{
    public static bool IsDebugEnabled = false; 
    private const uint RewardMultiplier = 10; 

    public ActivityData Data { get; set; }
    public TrialActivityInstance? TrialActivityInstance { get; set; }

    private static List<ActivityScheduleData>? _localSchedules = null;

    public ActivityManager(PlayerInstance player) : base(player)
    {
        Data = DatabaseHelper.Instance!.GetInstanceOrCreateNew<ActivityData>(player.Uid);
        if (Data.TrialActivityData.CurTrialStageId != 0) 
            TrialActivityInstance = new TrialActivityInstance(this);
        
        EnsureLocalConfigLoaded();
    }

    private void LogDebug(string message, bool isError = false)
    {
        if (!IsDebugEnabled) return;
        var logger = Logger.GetByClassName();
        if (isError) logger.Error($"[ActivityDebug] [Player:{Player.Uid}] {message}");
        else logger.Info($"[ActivityDebug] [Player:{Player.Uid}] {message}");
    }

    private void EnsureLocalConfigLoaded()
    {
        if (_localSchedules != null) return;
        try
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "ActivityConfig.json");
            if (!File.Exists(path)) return;

            string json = File.ReadAllText(path);
            var root = JsonConvert.DeserializeObject<dynamic>(json);
            if (root?.scheduleData != null)
            {
                _localSchedules = new List<ActivityScheduleData>();
                foreach (var item in root.scheduleData)
                {
                    _localSchedules.Add(new ActivityScheduleData {
                        ActivityId = (uint)item.activityId,
                        BeginTime = (long)item.beginTime,
                        EndTime = (long)item.endTime,
                        PanelId = (uint)item.panelId
                    });
                }
            }
        }
        catch (Exception ex) { LogDebug($"[配置加载失败] {ex.Message}", true); }
    }

 /// <summary>
    /// 获取当前有效的逻辑 ID。
    /// 既然 activityId 就是模块 ID，我们优先返回原始 ID，
    /// 如果数据库里存的是旧版 ID 或存在偏差，在这里做转换。
    /// </summary>
private uint GetMainId(uint activityId)
{
    // 1. 先去配置表找，看看 activityId 是不是某个配置的行 ID
    var config = GameData.ActivityLoginConfigData.Values.FirstOrDefault(x => (uint)x.ID == activityId);
    
    // 如果找到了，就把 activityId 替换成真正的业务模块 ID (如 1001801)
    uint realModuleId = config != null ? (uint)config.ActivityModuleID : activityId;

    // 2. 执行常规纠偏 (长 ID 转短 ID)
    uint shortId = realModuleId > 1000000 ? realModuleId / 100 : realModuleId;

    LogDebug($"[ID链路校验] 客户端输入:{activityId} -> 配置关联:{realModuleId} -> 最终存档Key:{shortId}");
    
    return shortId;
}
	// 在 ActivityManager 类中添加
	public async System.Threading.Tasks.Task SyncTrialActivity()
	{	
    	// 模仿签到领取后的行为：立即推送最新的全量数据
    	if (Player != null)
    	{
        await Player.SendPacket(new PacketGetTrialActivityDataScRsp(Player));
       
    	}
	}
   /// <summary>
    /// 修正：只要在配置表里定义的，就是有效的签到模块
    /// </summary>
   private bool IsCheckInActivity(uint id)
{
    // 同时检查原始 ID 和可能被除以 100 后的短 ID
    return GameData.ActivityLoginConfigData.Values.Any(x => 
        (uint)x.ActivityModuleID == id || (uint)x.ActivityModuleID / 100 == id);
}

    public void UpdateLoginDays()
    {
        if (Data?.LoginActivityData == null) return;
        var loginData = Data.LoginActivityData;
        var now = Extensions.GetUnixSec();

        if (loginData.LastUpdateTick == 0 || !UtilTools.IsSameDaily(loginData.LastUpdateTick, now))
        {
            if (_localSchedules == null) return;
            var activeSchedules = _localSchedules.Where(s => now >= s.BeginTime && now <= s.EndTime).ToList();
            
            bool updated = false;
            HashSet<uint> processedInThisTurn = new();

            foreach (var schedule in activeSchedules)
            {
                uint moduleId = GetMainId(schedule.ActivityId); // 使用纠偏后的 ID 存入数据库

                if (processedInThisTurn.Contains(moduleId)) continue;
                processedInThisTurn.Add(moduleId);

                if (IsCheckInActivity(moduleId))
                {
                    // 动态获取当前活动的最大奖励天数
                    var config = GameData.ActivityLoginConfigData.Values.FirstOrDefault(x => (uint)x.ActivityModuleID == moduleId);
                    int maxDays = config?.RewardList.Count ?? 7;

                    if (!loginData.LoginDays.ContainsKey(moduleId))
                    {
                        loginData.LoginDays[moduleId] = 1;
                        updated = true;
                    }
                    else if (loginData.LoginDays[moduleId] < (uint)maxDays)
                    {
                        loginData.LoginDays[moduleId]++;
                        updated = true;
                    }
                    LogDebug($"[进度自增] 模块:{moduleId}, 当前天数:{loginData.LoginDays[moduleId]}");
                }
            }

            if (updated)
            {
                loginData.LastUpdateTick = now;
                Save();
            }
        }
    }

public async Task<(ItemList items, uint panelId, uint retcode, uint finalId)> TakeLoginReward(uint activityId, uint takeDays)
{
    var items = new ItemList();
    
    // --- Step 1: 查找配置 (支持 ModuleID 1001801 或 行ID 1003) ---
    var loginConfig = GameData.ActivityLoginConfigData.Values.FirstOrDefault(x => 
        (uint)x.ActivityModuleID == activityId || 
        (uint)x.ID == activityId);

    if (loginConfig == null)
    {
        LogDebug($"[Step 1 错误] 配置表找不到 ID 或 ModuleID 为 {activityId}", true);
        return (items, 0, (uint)Retcode.RetCommonActivityNotOpen, activityId);
    }

    // --- Step 2: 确定数据库 Key ---
    // 统一通过映射后的 mainId (10018) 操作存档
    uint mainId = GetMainId(activityId); 
    
    LogDebug("================ [领取奖励调试] ================");
    LogDebug($"[状态] 客户端ID: {activityId} | 匹配Module: {loginConfig.ActivityModuleID} | 数据库Key: {mainId} | 申请天数: {takeDays}");

    var loginData = Data.LoginActivityData;
    if (loginData == null) return (items, 0, (uint)Retcode.RetPlayerDataError, activityId);

    // --- Step 3: 校验进度 ---
    if (!loginData.LoginDays.TryGetValue(mainId, out var currentDays))
    {
        LogDebug($"[Step 2 错误] 数据库找不到 Key: {mainId} 的进度记录", true);
        return (items, 0, (uint)Retcode.RetLoginActivityDaysLack, activityId);
    }

    if (takeDays > currentDays)
    {
        LogDebug($"[Step 2 错误] 进度不足！需要 {takeDays} 天，当前仅有 {currentDays} 天", true);
        return (items, 0, (uint)Retcode.RetLoginActivityDaysLack, activityId);
    }

    // --- Step 4: 校验重复领取 ---
    if (!loginData.TakenRewards.ContainsKey(mainId)) loginData.TakenRewards[mainId] = new List<uint>();
    if (loginData.TakenRewards[mainId].Contains(takeDays))
    {
        LogDebug($"[Step 3 错误] 3316：第 {takeDays} 天已领过", true);
        return (items, 0, (uint)Retcode.RetLoginActivityHasTaken, activityId);
    }

    // --- Step 5: 奖励索引安全检查 ---
    int rewardIndex = (int)takeDays - 1;
    if (rewardIndex < 0 || rewardIndex >= loginConfig.RewardList.Count)
    {
        LogDebug($"[Step 4 错误] 数组越界！RewardList 长度只有 {loginConfig.RewardList.Count}", true);
        return (items, 0, (uint)Retcode.RetReqParaInvalid, activityId);
    }

    // --- Step 6: 发放奖励 (10倍) ---
    int rewardId = loginConfig.RewardList[rewardIndex];
    if (GameData.RewardDataData.TryGetValue(rewardId, out var rewardDetail))
    {
        int mult = (int)RewardMultiplier;
        var rewardItemsToRequest = new List<ItemData>();
        
        var rawItems = new[] {
            (id: rewardDetail.ItemID_1, cnt: rewardDetail.Count_1),
            (id: rewardDetail.ItemID_2, cnt: rewardDetail.Count_2),
            (id: rewardDetail.ItemID_3, cnt: rewardDetail.Count_3),
            (id: rewardDetail.ItemID_4, cnt: rewardDetail.Count_4),
            (id: rewardDetail.ItemID_5, cnt: rewardDetail.Count_5),
            (id: rewardDetail.ItemID_6, cnt: rewardDetail.Count_6),
            (id: rewardDetail.Hcoin > 0 ? 1 : 0, cnt: rewardDetail.Hcoin)
        };

        foreach (var item in rawItems)
        {
            if (item.id <= 0 || item.cnt <= 0) continue;
            int finalNum = item.cnt * mult;
            rewardItemsToRequest.Add(new ItemData { ItemId = item.id, Count = finalNum });
            items.ItemList_.Add(new Item { ItemId = (uint)item.id, Num = (uint)finalNum });
        }

        if (rewardItemsToRequest.Count > 0 && Player.InventoryManager != null)
        {
            await Player.InventoryManager.AddItems(rewardItemsToRequest, notify: true);
        }
    }

    // --- Step 7: 更新存档并返回 ---
    loginData.TakenRewards[mainId].Add(takeDays);
    Save();

    // 确定返回给客户端的 PanelId，优先匹配 schedule 表
    uint currentPanelId = _localSchedules?.FirstOrDefault(s => s.ActivityId == activityId || s.PanelId == activityId)?.PanelId ?? activityId;
    
    LogDebug("================ [领取奖励成功] ================");
    
    // 关键：最后必须返回原始 activityId 匹配客户端请求
    return (items, currentPanelId, (uint)Retcode.RetSucc, activityId);
}

    private void Save()
    {
        if (!DatabaseHelper.ToSaveUidList.Contains(Player.Uid)) 
            DatabaseHelper.ToSaveUidList.Add(Player.Uid);
    }

 public GetLoginActivityScRsp GetLoginInfo()
{
    var rsp = new GetLoginActivityScRsp();
    if (Data?.LoginActivityData == null || _localSchedules == null) return rsp;

    var loginData = Data.LoginActivityData;

    foreach (var schedule in _localSchedules)
    {
        uint mainId = GetMainId(schedule.ActivityId);

        if (loginData.LoginDays.TryGetValue(mainId, out var days))
        {
            var activityProto = new EggLink.DanhengServer.Proto.LoginActivityData 
            {
                Id = schedule.ActivityId, 
                LoginDays = days,
                PanelId = schedule.PanelId == 0 ? schedule.ActivityId : schedule.PanelId
            };

            if (loginData.TakenRewards.TryGetValue(mainId, out var takenList))
            {
                // 【核心修正】：使用混淆后的字段名 JLHOGGDHMHG
                activityProto.JLHOGGDHMHG.AddRange(takenList); 
            }

            rsp.LoginActivityList.Add(activityProto);
            
            LogDebug($"[UI同步] ID:{activityProto.Id} | 进度:{days} | 已领天数:[{string.Join(",", activityProto.JLHOGGDHMHG)}]");
        }
    }
    return rsp;
}

    // 将此方法放回 ActivityManager 类中
public List<ActivityScheduleData> ToProto()
{
    // 返回本地加载的配置列表，供 PacketGetActivityScheduleConfigScRsp 使用
    return _localSchedules ?? new List<ActivityScheduleData>();
}
}
