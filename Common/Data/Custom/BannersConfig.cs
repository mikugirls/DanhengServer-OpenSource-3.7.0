using EggLink.DanhengServer.Database.Gacha;
using EggLink.DanhengServer.Enums;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util; // 引用全局Debug开关
using GachaInfo = EggLink.DanhengServer.Proto.GachaInfo;
using System.IO; 
using System.Text.Json; 
using System.Linq; 

namespace EggLink.DanhengServer.Data.Custom;

public class BannersConfig
{
    public List<BannerConfig> Banners { get; set; } = [];
}

public class BannerConfig
{
    private static string? _cachedHost;
    private static readonly object _configLock = new();

    public int GachaId { get; set; }
    public long BeginTime { get; set; }
    public long EndTime { get; set; }
    public GachaTypeEnum GachaType { get; set; }
    public List<int> RateUpItems5 { get; set; } = [];
    public List<int> RateUpItems4 { get; set; } = [];
    public int GetRateUpItem5Chance { get; set; } = 6;
    public int MaxCount { get; set; } = 90;
    public int EventChance { get; set; } = 50;

   public int DoGacha(List<int> goldAvatars, List<int> purpleAvatars, List<int> purpleWeapons, List<int> goldWeapons,
    List<int> blueWeapons, GachaData data, Random playerRand) // <-- 修改参数：传入持久化随机实例
{
    // --- 1. 获取水位逻辑 (保持不变) ---
    int pityCount = 0;
    if (this.GachaId == 4001) pityCount = data.NewbiePityCount;
    else if (this.GachaId == 1001) pityCount = data.StandardPityCount;
    else if (GachaType == GachaTypeEnum.AvatarUp) pityCount = data.LastAvatarGachaPity; 
    else if (GachaType == GachaTypeEnum.WeaponUp) pityCount = data.LastWeaponGachaPity;

    int currentMaxCount = (GachaType == GachaTypeEnum.WeaponUp) ? 80 : 90;
    int softPityStart5 = (GachaType == GachaTypeEnum.WeaponUp) ? 62 : 72; 

    // --- 2. 独立累加水位 (保持不变) ---
    if (this.GachaId == 4001) {
        data.NewbiePityCount++;
        data.NewbieGachaCount++; 
    }
    else if (this.GachaId == 1001) {
        data.StandardPityCount++;
        data.StandardCumulativeCount++; 
    }
    else if (GachaType == GachaTypeEnum.AvatarUp) {
        data.LastAvatarGachaPity++; 
    }
    else if (GachaType == GachaTypeEnum.WeaponUp) {
        data.LastWeaponGachaPity++; 
    }

    data.LastGachaPurpleFailedCount++; 

    // --- 3. 五星判定概率计算 ---
    double currentChance5 = GetRateUpItem5Chance / 1000.0; 
    if (pityCount >= softPityStart5) {
        currentChance5 += (GachaType == GachaTypeEnum.WeaponUp ? 0.07 : 0.06) * (pityCount - softPityStart5 + 1);
    }

    int item;
    // --- 4. 五星判定：使用 playerRand ---
    if (playerRand.NextDouble() < currentChance5 || pityCount + 1 >= currentMaxCount) {
        if (this.GachaId == 1001) data.StandardPityCount = 0;
        else if (this.GachaId == 4001) data.NewbiePityCount = 0;
        else if (GachaType == GachaTypeEnum.AvatarUp) data.LastAvatarGachaPity = 0;
        else if (GachaType == GachaTypeEnum.WeaponUp) data.LastWeaponGachaPity = 0;

        if (GachaType == GachaTypeEnum.WeaponUp) {
            // 注意：GetRateUpItem5 内部也需要同步修改为使用 playerRand
            item = GetRateUpItem5(goldWeapons, data.LastWeaponGachaFailed, playerRand);
            data.LastWeaponGachaFailed = !RateUpItems5.Contains(item);
        }
        else if (GachaType == GachaTypeEnum.AvatarUp) {
            item = GetRateUpItem5(goldAvatars, data.LastAvatarGachaFailed, playerRand);
            data.LastAvatarGachaFailed = !RateUpItems5.Contains(item);
        }
        else {
            item = GetRateUpItem5([.. goldAvatars, .. goldWeapons], false, playerRand);
        }
    }
    else {
        // --- 5. 四星及三星判定：使用 playerRand ---
        double currentChance4 = 0.051; 
        if (data.LastGachaPurpleFailedCount >= 9) currentChance4 = 0.51;

        if (playerRand.NextDouble() < currentChance4 || data.LastGachaPurpleFailedCount >= 10) {
            data.LastGachaPurpleFailedCount = 0;
            
            // 判定是否为 UP 项
            bool isUp = playerRand.Next(0, 100) < 50 && RateUpItems4.Count > 0;
            if (isUp) {
                item = RateUpItems4[playerRand.Next(0, RateUpItems4.Count)];
            }
            else {
                var pool = GachaType == GachaTypeEnum.AvatarUp ? (playerRand.Next(0, 2) == 0 ? purpleAvatars : purpleWeapons) : 
                          (GachaType == GachaTypeEnum.WeaponUp ? (playerRand.Next(0, 10) < 3 ? purpleAvatars : purpleWeapons) : 
                          [.. purpleAvatars, .. purpleWeapons]);
                item = pool[playerRand.Next(0, pool.Count)];
            }
        }
        else {
            // 三星逻辑
            item = blueWeapons[playerRand.Next(0, blueWeapons.Count)];
        }
    }
    
    return item;
}

    private void LogGacha(int uid, int item)
    {
        try 
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"GachaLog_{uid}.txt");
            string logEntry = $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Banner: {GachaId} | ItemID: {item}\n";
            File.AppendAllText(logPath, logEntry);
        }
        catch (Exception) { /* ignored */ }
    }

    public GachaInfo ToInfo(List<int> goldAvatar, int playerUid, GachaData data) 
    {
        if (_cachedHost == null)
        {
            lock (_configLock)
            {
                if (_cachedHost == null) _cachedHost = LoadHostFromConfig();
            }
        }
        string host = _cachedHost ?? "127.0.0.1:520"; 

        var info = new GachaInfo
        {
            GachaId = (uint)GachaId,
           
			GDIFAAHIFBH = (uint)data.NewbieGachaCount,						
            DetailWebview = $"http://{host}/gacha/history?id={GachaId}&uid={playerUid}",
            DropHistoryWebview = $"http://{host}/gacha/history?id={GachaId}&uid={playerUid}"
        };

        if (GachaType != GachaTypeEnum.Normal)
        {
            info.BeginTime = BeginTime;
            info.EndTime = EndTime;
        }

        if (RateUpItems4.Count > 0) info.ItemDetailList.AddRange(RateUpItems4.Select(id => (uint)id));
        if (RateUpItems5.Count > 0)
        {
            info.PrizeItemList.AddRange(RateUpItems5.Select(id => (uint)id));
            info.ItemDetailList.AddRange(RateUpItems5.Select(id => (uint)id));
        }

        if (GachaId == 1001)
        {
            info.GachaCeiling = new GachaCeiling
            {
                IsClaimed = data.IsStandardSelected,
                CeilingNum = (uint)data.StandardCumulativeCount, 
                AvatarList = { goldAvatar.Select(id => new GachaCeilingAvatar { AvatarId = (uint)id }) }
            };
        }
        return info;
    }

    private string? LoadHostFromConfig()
    {
        try 
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.json");
            if (File.Exists(configPath))
            {
                string jsonContent = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(jsonContent);
                var httpServer = doc.RootElement.GetProperty("HttpServer");
                string address = httpServer.GetProperty("PublicAddress").GetString() ?? "127.0.0.1";
                int port = httpServer.GetProperty("Port").GetInt32();
                return $"{address}:{port}";
            }
        }
        catch { /* ignored */ }
        return null;
    }

   public int GetRateUpItem5(List<int> gold, bool forceUp, Random playerRand)
{
    if (IsEvent(playerRand) || forceUp) 
        return RateUpItems5[playerRand.Next(0, RateUpItems5.Count)];
    return gold[playerRand.Next(0, gold.Count)];
}

public bool IsEvent(Random playerRand) => playerRand.Next(0, 100) < EventChance;
}