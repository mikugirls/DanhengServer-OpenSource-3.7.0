using System.IO;
using Newtonsoft.Json;
using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.Activity;
using EggLink.DanhengServer.GameServer.Game.Activity.Activities;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

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
    /// 改良版 ID 纠偏：优先匹配数据库中已有进度的活动
    /// </summary>
    private uint GetMainId(uint activityId)
    {
        uint rawId = activityId > 1000000 ? activityId / 100 : activityId;
        
        // 增加安全检查：确保 Data 和 LoginActivityData 不为空
        if (rawId < 10000 && Data?.LoginActivityData?.LoginDays != null)
        {
            var matchedId = Data.LoginActivityData.LoginDays.Keys
                .FirstOrDefault(k => IsCheckInActivity(k) && Data.LoginActivityData.LoginDays[k] > 0);
            
            if (matchedId != 0) return matchedId;

            // 兜底：找当前时间生效的第一个签到活动
            var now = Extensions.GetUnixSec();
            var active = _localSchedules?.FirstOrDefault(s => now >= s.BeginTime && now <= s.EndTime && IsCheckInActivity(GetMainId((uint)s.ActivityId)));
            if (active != null) return GetMainId((uint)active.ActivityId);
        }
        return rawId;
    }
	// 在 ActivityManager 类中添加
	public async Task SyncTrialActivity()
	{	
    	// 模仿签到领取后的行为：立即推送最新的全量数据
    	if (Player != null)
    	{
        await Player.SendPacket(new PacketGetTrialActivityDataScRsp(Player));
        LogDebug("[同步] 试用活动数据已全量同步");
    	}
	}
    private bool IsCheckInActivity(uint mainId)
    {
        if (mainId == 10014) return true;
        if (mainId < 10018) return false;
        return (mainId - 10018) % 5 == 0;
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
            foreach (var schedule in activeSchedules)
            {
                uint mainId = GetMainId((uint)schedule.ActivityId);
                if (IsCheckInActivity(mainId))
                {
                    if (!loginData.LoginDays.ContainsKey(mainId)) loginData.LoginDays[mainId] = 1;
                    else if (loginData.LoginDays[mainId] < 7) loginData.LoginDays[mainId]++;
                    updated = true;
                    LogDebug($"[进度更新] 活动:{mainId}, 当前天数:{loginData.LoginDays[mainId]}");
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
        uint mainId = GetMainId(activityId);
        
        // 使用本地配置或默认 ID
        uint currentPanelId = _localSchedules?.FirstOrDefault(s => GetMainId((uint)s.ActivityId) == mainId)?.PanelId ?? mainId;

        // 强校验：如果数据依然为空，安全返回
        if (Data?.LoginActivityData == null) 
        {
            LogDebug("[严重错误] 玩家活动数据为空", true);
            return (items, currentPanelId, 1, mainId);
        }
        
        var loginData = Data.LoginActivityData;

        // 1. 进度校验
        if (!loginData.LoginDays.TryGetValue(mainId, out var currentDays) || takeDays > currentDays)
        {
            LogDebug($"[领取拒绝] ID:{mainId} 进度不足", true);
            return (items, currentPanelId, 2602, mainId); 
        }

        // 2. 重复领取校验
        if (!loginData.TakenRewards.ContainsKey(mainId)) 
            loginData.TakenRewards[mainId] = new List<uint>();

        if (loginData.TakenRewards[mainId].Contains(takeDays)) 
        {
            LogDebug($"[重复拦截] ID:{mainId} 第 {takeDays} 天已领过");
            return (items, currentPanelId, 2002, mainId); 
        }

        // ... 后续逻辑保持不变 ...
        // 3. 奖励计算 (* 10)
        uint baseCount = takeDays switch { 1=>1, 2=>1, 3=>2, 4=>1, 5=>1, 6=>1, 7=>3, _=>0 };
        uint finalCount = baseCount * RewardMultiplier;

        if (finalCount > 0 && Player.InventoryManager != null)
        {
            items.ItemList_.Add(new Item { ItemId = 102, Num = finalCount });
            await Player.InventoryManager.AddItem(102, (int)finalCount, notify: true);
        }

        loginData.TakenRewards[mainId].Add(takeDays);
        Save();

        return (items, currentPanelId, 0, mainId); 
    }

    private void Save()
    {
        if (!DatabaseHelper.ToSaveUidList.Contains(Player.Uid)) 
            DatabaseHelper.ToSaveUidList.Add(Player.Uid);
    }

    public GetLoginActivityScRsp GetLoginInfo()
    {
        var rsp = new GetLoginActivityScRsp();
        if (Data?.LoginActivityData == null) return rsp;
        var loginProtoData = Data.LoginActivityData.ToProto();
        foreach (var proto in loginProtoData)
        {
            var config = _localSchedules?.FirstOrDefault(s => GetMainId((uint)s.ActivityId) == proto.Id);
            if (config != null) proto.PanelId = (uint)config.PanelId;
        }
        rsp.LoginActivityList.AddRange(loginProtoData);
        return rsp;
    }

    public List<ActivityScheduleData> ToProto()
    {
        return _localSchedules ?? new List<ActivityScheduleData>();
    }
}
