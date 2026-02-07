using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Enums.Item;
using EggLink.DanhengServer.Util;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("MappingInfo.json")]
public class MappingInfoExcel : ExcelResource
{
    public int ID { get; set; }
    public int WorldLevel { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public FarmTypeEnum FarmType { get; set; } = FarmTypeEnum.None; // is enum

    public List<MappingInfoItem> DisplayItemList { get; set; } = [];

    [JsonIgnore] public List<MappingInfoItem> DropItemList { get; set; } = [];

    [JsonIgnore] public List<MappingInfoItem> DropRelicItemList { get; set; } = [];

    public override int GetId()
    {
        return ID * 10 + WorldLevel;
    }
	public override void Loaded()
    {
        // 1. 获取当前 Key 并存入全局字典
        // 使用索引器赋值防止重复加载导致的 Key 冲突报错
        GameData.MappingInfoData[GetId()] = this;

        // 2. 【核心修复逻辑】：针对 ELEMENT (进阶材料) 数据缺失的“影子补全”
        // 如果当前副本列表为空（常见于 WL 0, 1, 6），则尝试从其他均衡等级借用 ID 模板
       if (this.DisplayItemList.Count == 0 && 
       (this.FarmType == FarmTypeEnum.ELEMENT || this.FarmType == FarmTypeEnum.COCOON))
        {
            // 轮询 WL 2, 3, 4, 5，直到找到一个含有材料 ID (如虚幻铸铁 110406) 的模板
            for (int i = 1; i <= 5; i++)
            {
                int templateKey = this.ID * 10 + i;
                if (GameData.MappingInfoData.TryGetValue(templateKey, out var template) && template.DisplayItemList.Count > 0)
                {
                    // 使用 new List 进行深拷贝，确保修改当前对象的 DropItemList 不会干扰到种子模板
                    this.DisplayItemList = new List<MappingInfoItem>(template.DisplayItemList);
                    break; 
                }
            }
        }

        // 3. 原有的空检查。现在 ELEMENT 类型即便是 WL 0/1/6 也已经有了借来的 ID 列表。
        if (DisplayItemList.Count == 0) return;
		
        List<int> equipDrop = [];
        Dictionary<int, List<int>> relicDrop = [];

        foreach (var item in DisplayItemList)
        {
            // 数量大于 0 的直接添加 (通常是开拓经验)
            if (item.ItemNum > 0)
            {
                DropItemList.Add(item);
                continue;
            }

            // 信用点动态计算
            if (item.ItemID == 2)
            {
                DropItemList.Add(new MappingInfoItem()
                {
                    ItemID = 2,
                    MinCount = (50000 + WorldLevel * 50000) * (int)FarmType,
                    MaxCount = (100000 + WorldLevel * 50000) * (int)FarmType
                });
                continue;
            }

            GameData.ItemConfigData.TryGetValue(item.ItemID, out var excel);
            if (excel == null) continue;
			//Console.WriteLine($"ID:{ID} WL:{WorldLevel} Added Material:{excel.ID}");
            // 遗器展示逻辑
            if (excel.ItemSubType == ItemSubTypeEnum.RelicSetShowOnly)
            {
                var baseRelicId = item.ItemID / 10 % 1000;
                var baseRarity = item.ItemID % 10;
                var relicStart = 20001 + baseRarity * 10000 + baseRelicId * 10;
                var relicEnd = relicStart + 3;
                for (; relicStart <= relicEnd; relicStart++)
                {
                    GameData.ItemConfigData.TryGetValue(relicStart, out var relicExcel);
                    if (relicExcel == null) break;

                    if (!relicDrop.TryGetValue(baseRarity, out _))
                    {
                        var value = new List<int>();
                        relicDrop[baseRarity] = value;
                    }
                    relicDrop[baseRarity].Add(relicStart);
                }
            }
            // 材料类计算
            else if (excel.ItemMainType == ItemMainTypeEnum.Material)
            {
                MappingInfoItem? drop = null;
               switch (excel.PurposeType)
	{
   case 1: // 角色经验/光锥经验
        int minCount = 1;
        int maxCount = 1;

        switch (excel.Rarity)
        {
            case ItemRarityEnum.NotNormal: // 绿色
                minCount = (WorldLevel < 3) ? 10 : 15;
                maxCount = (WorldLevel < 3) ? 30 : 45;
                break;
            case ItemRarityEnum.Normal: // 蓝色
                minCount = (WorldLevel < 3) ? 5 : 10;
                maxCount = (WorldLevel < 3) ? 15 : 25;
                break;
            case ItemRarityEnum.Rare: // 紫色
                minCount = (WorldLevel < 3) ? 0 : WorldLevel;
                maxCount = (WorldLevel < 3) ? 0 : WorldLevel * 2;
                break;
            default:
                minCount = 1;
                maxCount = 2;
                break;
        }

        // 【关键改动】：如果该等级不掉落，仅将 drop 设为 null，而不是 return
        if (maxCount > 0)
        {
            drop = new MappingInfoItem(excel.ID, 0) 
            { 
                Chance = 100, 
                MinCount = minCount, 
                MaxCount = maxCount 
            };
        }
        break; // 这里一个 break 结束 case 1

    case 2: // 晋阶材料 (大世界BOSS/虚幻铸铁等)
        // 官服逻辑：必掉，数量 2-3 或 4-5
        int bossCount = WorldLevel >= 4 ? 5 : (WorldLevel >= 2 ? 3 : 2);
        drop = new MappingInfoItem(excel.ID, bossCount)
        {
            Chance = 100, 
            MinCount = bossCount,
            MaxCount = (WorldLevel >= 3) ? bossCount + 1 : bossCount
        };
        break;

  
case 3: // 行迹材料 (花萼赤)
    // 1. 定义数量逻辑
    int finalMin = 1;
    int finalMax = 1;

    // --- 绿色：NotNormal (最低级) ---
    if (excel.Rarity == ItemRarityEnum.NotNormal) 
    {
        // 满足你的需求：均衡等级 1 掉落 10 个
        // 这里的 WorldLevel 已经是你修正偏移（+1）后的值了
        finalMin = WorldLevel switch
        {
            0 => 5,   // 以防万一还是 0
            1 => 10,  // 均衡等级 1 必掉 10 个
            2 => 12,
            3 => 15,
            4 => 18,
            >= 5 => 20, // 均衡 5-6 掉 20 个
            _ => 10
        };
        finalMax = finalMin; // 如果想随机掉 10-12 个，可以写 finalMin + 2
    }
    // --- 蓝色：Normal (中级) ---
    else if (excel.Rarity == ItemRarityEnum.Normal) 
    {
        finalMin = (WorldLevel >= 1) ? 2 : 1; 
        finalMax = finalMin + 2; 
    }

    // 2. 概率逻辑
    int traceChance = excel.Rarity switch
    {
        ItemRarityEnum.NotNormal => 100, // 绿色必须 100% 必掉
        ItemRarityEnum.Normal => Math.Min(100, 50 + (WorldLevel * 10)), 
        ItemRarityEnum.Rare => 10 + (WorldLevel * 5),
        _ => 100
    };

    // 3. 【关键修正点】：第二个参数传 0 ！！
    // 只有传 0，HandleMappingInfo 才会去读取 MinCount 和 MaxCount
    drop = new MappingInfoItem(excel.ID, 0) 
    { 
        Chance = traceChance,
        MinCount = finalMin,
        MaxCount = finalMax 
    };
    break;

    case 5: // 光锥经验 (提纯以太)
        // 数量略有随机
        var lcExpCount = excel.Rarity switch
        {
            ItemRarityEnum.NotNormal => 3,
            ItemRarityEnum.Rare => WorldLevel >= 3 ? 1 : 0,
            _ => 2
        };
        drop = new MappingInfoItem(excel.ID, (int)lcExpCount) 
        { 
            Chance = (excel.Rarity == ItemRarityEnum.Rare) ? (WorldLevel * 15) : 100 
        };
        break;
	case 7: // 普通怪物掉落素材 (例如：工造机杼、永寿幼芽等)
        // 逻辑：这类材料通常掉落数量较多，且随均衡等级提升
        int materialMin = 1;
        int materialMax = 1;

        switch (excel.Rarity)
        {
            case ItemRarityEnum.NotNormal: // 绿色品质 (ID: 113011)
                materialMin = 5 + WorldLevel * 3; // WL1: 8, WL6: 23
                materialMax = 10 + WorldLevel * 5; // WL1: 15, WL6: 40
                break;
            case ItemRarityEnum.Normal: // 蓝色品质
                materialMin = 1 + (WorldLevel / 2); 
                materialMax = 3 + WorldLevel;
                break;
            case ItemRarityEnum.Rare: // 紫色品质
                materialMin = WorldLevel >= 3 ? 1 : 0;
                materialMax = WorldLevel >= 3 ? (WorldLevel - 1) : 0;
                break;
            default:
                materialMin = 1;
                materialMax = 2;
                break;
        }

        // 只有最大数量大于0时才添加掉落
        if (materialMax > 0)
        {
            drop = new MappingInfoItem(excel.ID, 0) 
            { 
                Chance = 100, // 基础材料通常设为必掉，由逻辑控制数量
                MinCount = materialMin,
                MaxCount = materialMax 
            };
        }
        break;
		
    case 11: // 遗器合成材料 (残骸)
        drop = new MappingInfoItem(excel.ID, 10) { Chance = 100 };
        break;
	case 17: // 合成材料 (如：ID 181013 等)
        // 逻辑：这类材料通常是大世界消耗品合成所需，掉落量波动较大
        int composeMin = 1;
        int composeMax = 1;

        switch (excel.Rarity)
        {
            case ItemRarityEnum.NotNormal: // 绿色品质
                // 随均衡等级 (WorldLevel) 提升掉落基数
                composeMin = 2 + WorldLevel; 
                composeMax = 5 + (WorldLevel * 2);
                break;
            case ItemRarityEnum.Normal: // 蓝色品质
                composeMin = 1 + (WorldLevel / 3);
                composeMax = 2 + (WorldLevel / 2);
                break;
            default:
                composeMin = 1;
                composeMax = 2;
                break;
        }

        // 构造掉落条目
        drop = new MappingInfoItem(excel.ID, 0)
        {
            Chance = 100, // 合成材料通常作为副产物 100% 掉落
            MinCount = composeMin,
            MaxCount = composeMax
        };
        break;
    default:
        drop = new MappingInfoItem(excel.ID, 1) { Chance = 100 };
        break;
	}

                if (drop != null) DropItemList.Add(drop);
            }
            else if (excel.ItemMainType == ItemMainTypeEnum.Equipment)
            {
                equipDrop.Add(excel.ID);
            }
        }

        if (equipDrop.Count > 0)
		{
    foreach (var dropId in equipDrop)
    {
        // 官服逻辑：均衡等级越高，掉狗粮的概率稍微提升一点
        // WL0: 15% | WL3: 21% | WL6: 27%
        int lcChance = 15 + (WorldLevel * 2); 

        MappingInfoItem d = new(dropId, 1) 
        { 
        Chance = lcChance 
        };
        DropItemList.Add(d);
    }
		}

        // 处理遗器具体掉落数量
        if (relicDrop.Count > 0)
        {
            foreach (var entry in relicDrop)
            {
                foreach (var value in entry.Value)
                {
                    MappingInfoItem d = new(value, 1);
                    var relicAmount = entry.Key switch
                    {
                        4 => WorldLevel * 0.5 - 0.5,
                        3 => WorldLevel * 0.5 + (WorldLevel == 2 ? 1.0 : 0),
                        2 => 6 - WorldLevel + 0.5 - (WorldLevel == 1 ? 3.75 : 0),
                        _ => WorldLevel == 1 ? 6 : 2
                    };
                    if (relicAmount > 0)
                    {
                        d.ItemNum = (int)relicAmount;
                        DropRelicItemList.Add(d);
                    }
                }
            }
        }
    }
    

    public List<ItemData> GenerateRelicDrops()
    {
        var relicsMap = new Dictionary<int, List<MappingInfoItem>>();
        foreach (var relic in DropRelicItemList)
        {
            GameData.ItemConfigData.TryGetValue(relic.ItemID, out var itemData);
            if (itemData == null) continue;
            switch (itemData.Rarity)
            {
                case ItemRarityEnum.NotNormal:
                    AddRelicToMap(relic, 2, relicsMap);
                    break;
                case ItemRarityEnum.Rare:
                    AddRelicToMap(relic, 3, relicsMap);
                    break;
                case ItemRarityEnum.VeryRare:
                    AddRelicToMap(relic, 4, relicsMap);
                    break;
                case ItemRarityEnum.SuperRare:
                    AddRelicToMap(relic, 5, relicsMap);
                    break;
                default:
                    continue;
            }
        }

        List<ItemData> drops = [];
        // Add higher rarity relics first
        for (var rarity = 5; rarity >= 2; rarity--)
        {
            var count = GetRelicCountByWorldLevel(rarity) *
                        ConfigManager.Config.ServerOption.ValidFarmingDropRate();
            if (count <= 0) continue;
            if (!relicsMap.TryGetValue(rarity, out var value)) continue;
            if (value.IsNullOrEmpty()) continue;
            while (count > 0)
            {
                var relic = value.RandomElement();
                drops.Add(new ItemData
                {
                    ItemId = relic.ItemID,
                    Count = 1
                });
                count--;
            }
        }

        return drops;
    }

    private void AddRelicToMap(MappingInfoItem relic, int rarity, Dictionary<int, List<MappingInfoItem>> relicsMap)
    {
        if (relicsMap.TryGetValue(rarity, out var value))
            value.Add(relic);
        else
            relicsMap.Add(rarity, [relic]);
    }

    private int GetRelicCountByWorldLevel(int rarity)
    {
        return WorldLevel switch
        {
            1 => rarity switch
            {
                2 => 6,
                3 => 3,
                4 => 1,
                5 => 0,
                _ => 0
            },
            2 => rarity switch
            {
                2 => 2,
                3 => 4,
                4 => 2 + LuckyRelicDropped(),
                5 => 0,
                _ => 0
            },
            3 => rarity switch
            {
                2 => 0,
                3 => 4,
                4 => 2,
                5 => 1,
                _ => 0
            },
            4 => rarity switch
            {
                2 => 0,
                3 => 3,
                4 => 2 + LuckyRelicDropped(),
                5 => 1 + LuckyRelicDropped(),
                _ => 0
            },
            5 => rarity switch
            {
                2 => 0,
                3 => 1 + LuckyRelicDropped(),
                4 => 3,
                5 => 2,
                _ => 0
            },
            6 => rarity switch
            {
                2 => 0,
                3 => 0,
                4 => 5,
                5 => 2 + LuckyRelicDropped(),
                _ => 0
            },
            _ => 0
        };
    }

    private int LuckyRelicDropped()
    {
        return Random.Shared.Next(100) < 25 ? 1 : 0;
    }
}

public class MappingInfoItem
{
    public MappingInfoItem()
    {
    }

    public MappingInfoItem(int itemId, int itemNum)
    {
        ItemID = itemId;
        ItemNum = itemNum;
    }

    public int ItemID { get; set; }
    public int ItemNum { get; set; }

    public int MinCount { get; set; }
    public int MaxCount { get; set; }
    public int Chance { get; set; } = 100;
}
