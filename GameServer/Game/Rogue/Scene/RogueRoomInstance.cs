using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Game.Rogue.Scene;

public class RogueRoomInstance
{
    public int MonsterLevel { get; set; } 
    public int MapId { get; set; }

    public RogueRoomInstance(RogueMapExcel excel, RogueAreaConfigExcel areaConfig)
    {
        SiteId = excel.SiteID;
        NextSiteIds = excel.NextSiteIDList;
        MapId = excel.RogueMapID;
        
        Status = excel.IsStart ? RogueRoomStatus.Unlock : RogueRoomStatus.Lock;

        // --- 1. 获取世界索引 ---
        int areaId = areaConfig.RogueAreaID; 
        int worldIndex = (areaId / 10) % 10; 

        // [日志 1] 构造函数被调用
        // 注意：这里打印太频繁可能会刷屏，所以我只在关键 SiteID (比如第1关或第13关) 打印
        if (SiteId == 1 || SiteId == 13)
        {
            Console.WriteLine($"[RogueRoomDebug] 正在生成房间 SiteId: {SiteId}, MapId: {MapId}, World: {worldIndex}");
        }

        // --- 2. 核心逻辑 ---
        bool isBossRoom = SiteId == 13 || SiteId == 111 || SiteId == 112;

        if (isBossRoom)
        {
            // 【策略 A】BOSS 房
            RoomId = GetBossRoomIdByWorldIndex(worldIndex);
            Console.WriteLine($"[RogueRoomDebug] BOSS房锁定 -> SiteId: {SiteId}, 强制 RoomId: {RoomId}");
        }
        else
        {
            // 【策略 B】普通房间
            if (MapId == 1)
            {
                RoomId = 100 + (SiteId - 1);
                if (!GameData.RogueRoomData.ContainsKey(RoomId)) RoomId = 100;
            }
            else if (MapId < 100)
            {
                RoomId = MapId * 100 + SiteId;
            }
            else
            {
                int prefix = GetPrefixByWorldIndex(worldIndex);
                int calculatedRoomId = prefix * 100 + SiteId;

                if (SiteId == 1) // 调试日志
                {
                    Console.WriteLine($"[RogueRoomDebug] 普通房计算 -> World: {worldIndex}, Prefix: {prefix}, 尝试 RoomId: {calculatedRoomId}");
                }

                if (GameData.RogueRoomData.ContainsKey(calculatedRoomId))
                {
                    RoomId = calculatedRoomId;
                }
                else
                {
                    // 兜底逻辑
                    int fallbackId = MapId * 100 + SiteId;
                    if (GameData.RogueRoomData.ContainsKey(fallbackId))
                    {
                        RoomId = fallbackId;
                        Console.WriteLine($"[RogueRoomDebug] RoomId {calculatedRoomId} 不存在! 回退到Map原生: {fallbackId}");
                    }
                    else
                    {
                        RoomId = GameData.RogueMapGenData.TryGetValue(SiteId, out var genData) 
                                 ? genData.RandomElement() : SiteId;
                        Console.WriteLine($"[RogueRoomDebug] 彻底找不到! 随机生成: {RoomId}");
                    }
                }
            }
        }

        // --- 3. 加载数据 ---
        if (GameData.RogueRoomData.TryGetValue(RoomId, out var roomExcel))
        {
            Excel = roomExcel;
            // [关键日志] 最终确认房间类型
            if (SiteId == 13) 
            {
                Console.WriteLine($"[RogueRoomDebug] 最终加载房间 Excel -> RoomId: {RoomId}, Type: {Excel.RogueRoomType} (7=BOSS)");
            }
        }
        else
        {
            Excel = GameData.RogueRoomData.Values.FirstOrDefault() ?? new RogueRoomExcel();
            RoomId = Excel.RogueRoomID;
            Console.WriteLine($"[RogueRoomDebug] 严重错误! 房间数据加载失败, 使用默认 RoomId: {RoomId}");
        }

      // --- 4. 【等级计算 - 终极完美版】 ---
        // 现在 RogueAreaConfigExcel 里有 RecommendLevel 了，直接读！
        int baseLevel = areaConfig.RecommendLevel;

        // 兜底：万一 JSON 里某些难度没填等级，就用难度估算
        if (baseLevel == 0) baseLevel = areaConfig.Difficulty * 10;

        // 加上层数成长 (每2层+1级)，保证后期稍微难一点
        this.MonsterLevel = baseLevel + (SiteId > 1 ? SiteId / 2 : 0);
        
        // 调试日志
        if (SiteId == 1)
            Console.WriteLine($"[RogueLevel] 区域 {areaConfig.RogueAreaID} -> 推荐等级: {baseLevel}, 初始实装: {this.MonsterLevel}");
    }

    public int RoomId { get; set; }
    public int SiteId { get; set; }
    public RogueRoomStatus Status { get; set; } = RogueRoomStatus.Lock;
    public List<int> NextSiteIds { get; set; }
    public RogueRoomExcel Excel { get; set; }

   // GameServer/Game/Rogue/Scene/RogueRoomInstance.cs

   // GameServer/Game/Rogue/Scene/RogueRoomInstance.cs

    private int GetPrefixByWorldIndex(int index)
{
    return index switch
    {
        1 => 3,    // 世界 1 (基座舱段)
        2 => 3,    // 世界 2 (基座舱段)
        3 => 1111, // 世界 3: 空间站/贝洛伯格上层
        4 => 2111, // 世界 4: 贝洛伯格下层 (铆钉镇风格)
        5 => 1321, // 世界 5: 仙舟罗浮 (对应卡芙卡风格)
        6 => 1221, // 世界 6: 贝洛伯格雪山 (对应可可利亚风格)
        7 => 1311, // 世界 7: 仙舟罗浮 (对应玄鹿风格)
        8 => 2311, // 世界 8: 仙舟罗浮 (对应彦卿风格)
        9 => 3111, // 世界 9: 匹诺康尼 (对应死亡风格)
        _ => 1111
    };
}

   private int GetBossRoomIdByWorldIndex(int index)
{
    return index switch
    {
        1 => 307,
       
        // 修正第二世界：使用专属大房间 200713
        2 => 200713, // 对应 JSON 中 ContentID 改为 12901 (自动机兵•灰熊（完整)
        3 => 111713, // 对应 JSON 中 ContentID 改为 13901 (杰帕德)
        4 => 211713, // 对应 JSON 中 ContentID 改为 14901 (史瓦罗) - 保持下层区风格
        5 => 132713, // 对应 JSON 中 ContentID 改为 15901 (卡芙卡) - 回归仙舟风格
        6 => 122713, // 对应 JSON 中 ContentID 改为 16901 (可可利亚) - 雪山风格
        7 => 222713, // 对应 JSON 中 ContentID 改为 17901 (丰饶玄鹿)
        8 => 231713, // 对应 JSON 中 ContentID 改为 18901 (彦卿)
        9 => 311713, // 对应 JSON 中 ContentID 改为 19901 (何物朝向死亡)
        _ => 111713
    };
}

    public RogueRoom ToProto()
    {
        return new RogueRoom
        {
            RoomId = (uint)RoomId,
            SiteId = (uint)SiteId,
            CurStatus = Status,
            IMIMGFAAGHM = (uint)Excel.RogueRoomType
        };
    }
}
