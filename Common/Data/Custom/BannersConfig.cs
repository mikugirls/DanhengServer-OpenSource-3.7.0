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
    List<int> blueWeapons, GachaData data, Random playerRand)
{
    // --- 1. 获取当前卡池水位 (加上 ?? 0 保护) ---
    int pityCount = 0;
    if (this.GachaId == 4001) pityCount = data.NewbiePityCount ?? 0;
    else if (this.GachaId == 1001) pityCount = data.StandardPityCount ?? 0;
    else if (GachaType == GachaTypeEnum.AvatarUp) pityCount = data.LastAvatarGachaPity ?? 0; 
    else if (GachaType == GachaTypeEnum.WeaponUp) pityCount = data.LastWeaponGachaPity ?? 0;

    int currentMaxCount = (GachaType == GachaTypeEnum.WeaponUp) ? 80 : 90;
    int softPityStart5 = (GachaType == GachaTypeEnum.WeaponUp) ? 62 : 72; 

    // --- 2. 独立累加水位 (判定前自增) ---
    if (this.GachaId == 4001) { 
        data.NewbiePityCount = (data.NewbiePityCount ?? 0) + 1; 
        data.NewbieGachaCount = (data.NewbieGachaCount ?? 0) + 1; 
    }
    else if (this.GachaId == 1001) { 
        data.StandardPityCount = (data.StandardPityCount ?? 0) + 1; 
        data.StandardCumulativeCount = (data.StandardCumulativeCount) + 1; // 该字段本身不为 null
    }
    else if (GachaType == GachaTypeEnum.AvatarUp) data.LastAvatarGachaPity = (data.LastAvatarGachaPity ?? 0) + 1; 
    else if (GachaType == GachaTypeEnum.WeaponUp) data.LastWeaponGachaPity = (data.LastWeaponGachaPity ?? 0) + 1;

    data.LastGachaPurpleFailedCount = (data.LastGachaPurpleFailedCount ?? 0) + 1; 

    // --- 3. 五星判定概率计算 ---
    double currentChance5 = GetRateUpItem5Chance / 1000.0; 
    if (pityCount >= softPityStart5) {
        currentChance5 += (GachaType == GachaTypeEnum.WeaponUp ? 0.07 : 0.06) * (pityCount - softPityStart5 + 1);
    }

    int item;
    
    // --- 4. 五星判定逻辑 ---
    if (playerRand.NextDouble() < currentChance5 || pityCount + 1 >= currentMaxCount) 
    {
        // 精准重置水位
        if (this.GachaId == 1001) data.StandardPityCount = 0;
        else if (this.GachaId == 4001) data.NewbiePityCount = 0;
        else if (GachaType == GachaTypeEnum.AvatarUp) data.LastAvatarGachaPity = 0;
        else if (GachaType == GachaTypeEnum.WeaponUp) data.LastWeaponGachaPity = 0;

        if (GachaType == GachaTypeEnum.AvatarUp) 
        {
            bool isUp = (data.LastAvatarGachaFailed ?? false) || playerRand.Next(0, 100) < 50;
            if (isUp && RateUpItems5.Count > 0) {
                item = RateUpItems5[playerRand.Next(0, RateUpItems5.Count)];
                data.LastAvatarGachaFailed = false;
            } else {
                item = goldAvatars[playerRand.Next(0, goldAvatars.Count)];
                data.LastAvatarGachaFailed = true;
            }
        }
        else if (GachaType == GachaTypeEnum.WeaponUp) 
        {
            // 75% 概率
            bool isUp = (data.LastWeaponGachaFailed ?? false) || playerRand.Next(0, 100) < 75;
            if (isUp && RateUpItems5.Count > 0) {
                item = RateUpItems5[playerRand.Next(0, RateUpItems5.Count)];
                data.LastWeaponGachaFailed = false;
            } else {
                item = goldWeapons[playerRand.Next(0, goldWeapons.Count)];
                data.LastWeaponGachaFailed = true;
            }
        }
        else if (GachaType == GachaTypeEnum.Newbie) 
        {
            item = goldAvatars[playerRand.Next(0, goldAvatars.Count)];
        }
        else 
        {
            // 常驻池 50/50
            if (playerRand.Next(0, 100) < 50) item = goldAvatars[playerRand.Next(0, goldAvatars.Count)];
            else item = goldWeapons[playerRand.Next(0, goldWeapons.Count)];
        }
    }
    else 
    {
        // --- 5. 四星判定逻辑 ---
        double currentChance4 = 0.051; 
        if ((data.LastGachaPurpleFailedCount ?? 0) >= 9) currentChance4 = 0.51; 

        if (playerRand.NextDouble() < currentChance4 || (data.LastGachaPurpleFailedCount ?? 0) >= 10) 
        {
            data.LastGachaPurpleFailedCount = 0; 
            bool isUp = (data.LastPurpleGachaFailed ?? false) || playerRand.Next(0, 100) < 50;

            if (isUp && RateUpItems4.Count > 0) 
            {
                item = RateUpItems4[playerRand.Next(0, RateUpItems4.Count)];
                data.LastPurpleGachaFailed = false;
            }
            else 
            {
                if (GachaType == GachaTypeEnum.AvatarUp)
                {
                    if (playerRand.Next(0, 100) < 50) item = purpleAvatars[playerRand.Next(0, purpleAvatars.Count)];
                    else item = purpleWeapons[playerRand.Next(0, purpleWeapons.Count)];
                }
                else if (GachaType == GachaTypeEnum.WeaponUp)
                {
                    // 75% 武器权重
                    if (playerRand.Next(0, 100) < 75) item = purpleWeapons[playerRand.Next(0, purpleWeapons.Count)];
                    else item = purpleAvatars[playerRand.Next(0, purpleAvatars.Count)];
                }
                else
                {
                    List<int> pool = [.. purpleAvatars, .. purpleWeapons]; 
                    item = pool[playerRand.Next(0, pool.Count)];
                }
                data.LastPurpleGachaFailed = true; 
            }
        }
        else 
        {
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
           
			GDIFAAHIFBH = (uint)(data.NewbieGachaCount ?? 0),					
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
                IsClaimed = data.IsStandardSelected ?? false,
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