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
            // // 世界1 前缀 3 (对应 301)
            3 => 3, 
            
            // 世界 4: 也用 2111 (大房间前缀)
            4 => 2111, 
            
            // 其他保持原样
            6 => 1221,
            7 => 1321,
            8 => 2001,
            _ => 1111
        };
    }

    private int GetBossRoomIdByWorldIndex(int index)
    {
        return index switch
        {
            //
            3 => 307, 
            
            // 世界 4 (史瓦罗): 也用 211713 (大房间)
            // 没错，两个世界共用同一个房间地图！区别在于刷出来的怪。
            4 => 131713,
			5 => 132713,
            
            // 其他
            6 => 122713,
            7 => 132713,
            8 => 200713,
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