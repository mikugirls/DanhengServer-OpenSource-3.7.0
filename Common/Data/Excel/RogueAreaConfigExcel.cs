using Newtonsoft.Json;
using System.Collections.Generic;

namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("RogueAreaConfig.json")]
public class RogueAreaConfigExcel : ExcelResource
{
    // 区域 ID (如 130, 131...)
    public int RogueAreaID { get; set; }

    // 区域进度/世界 ID (如 1, 2, 3...)
    public int AreaProgress { get; set; }

    // 难度 (1, 2, 3, 4, 5)
    public int Difficulty { get; set; }
	// --- 【新增这行】 ---
    public int RecommendLevel { get; set; }
    // 首通奖励 ID
    public int FirstReward { get; set; }
	
	public Dictionary<int, int> DisplayMonsterMap { get; set; } = new();
    // 沉浸器掉落显示 ID (关键字段)
    public int MonsterEliteDropDisplayID { get; set; }

    // 积分映射
    public Dictionary<int, int> ScoreMap { get; set; } = new();

    // 【核心修复】这里必须用 RogueChestItem，不能用 ItemData
    // 否则 JSON 里的 "ItemID" 和 "ItemNum" 无法被正确读取
    public List<RogueChestItem> ChestDisplayItemList { get; set; } = new();

    // --- 内部逻辑字段 (不从 JSON 读取) ---

    [JsonIgnore] 
    public int MapId { get; set; }

    [JsonIgnore] 
    public Dictionary<int, RogueMapExcel> RogueMaps { get; set; } = new();

    public override int GetId()
    {
        return RogueAreaID;
    }

    public override void Loaded()
    {
        // 注册到 GameData
        if (!GameData.RogueAreaConfigData.ContainsKey(RogueAreaID))
        {
            GameData.RogueAreaConfigData.Add(RogueAreaID, this);
        }

        // 计算 MapId (根据你的逻辑)
        // 注意：这里的公式视具体 MapData.json 的 Key 而定
        // 如果你的 MapData Key 是 1, 2, 3... 这种，这里可能需要调整
        // 暂时保留你原来的写法
        MapId = AreaProgress * 100 + Difficulty; 
    }

    public override void AfterAllDone()
    {
        // 关联 RogueMap 数据
        if (GameData.RogueMapData.TryGetValue(MapId, out var map))
        {
            RogueMaps = map;
        }
    }
}

/// <summary>
/// 专门用于解析 RogueAreaConfig.json 中 ChestDisplayItemList 的辅助类
/// 严格对应 JSON 的字段名 (ItemID, ItemNum)
/// </summary>
public class RogueChestItem
{
    // 对应 JSON 中的 "ItemID" (大写 ID)
    public int ItemID { get; set; }

    // 对应 JSON 中的 "ItemNum"
    public int ItemNum { get; set; }
}
