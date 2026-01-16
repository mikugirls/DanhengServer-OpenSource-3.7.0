using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Game.Rogue.Scene;

public class RogueRoomInstance
{
    public int MonsterLevel { get; set; } 

    // --- 新增：保存当前地图ID，方便调试和逻辑判断 ---
    public int MapId { get; set; }

    /// <summary>
    /// 初始化模拟宇宙房间
    /// </summary>
    /// <param name="excel">来自 RogueMap.json 的点位信息 (IsStart, PosX, NextSiteIDList等)</param>
    /// <param name="areaConfig">传入完整的区域配置以获取难度和进度</param>
    public RogueRoomInstance(RogueMapExcel excel, RogueAreaConfigExcel areaConfig)
    {
        SiteId = excel.SiteID;
        NextSiteIds = excel.NextSiteIDList;
        MapId = excel.RogueMapID; // 记录 MapID

        // 初始状态：起点设为解锁，其余锁定
        Status = excel.IsStart ? RogueRoomStatus.Unlock : RogueRoomStatus.Lock;

        // --- 核心计算：房间前缀 (Prefix) ---
        // 我们发现房间ID = Prefix * 100 + SiteID
        // 比如 MapID 200 -> Prefix 1111 -> Room 111101
        int roomPrefix = GetRoomPrefixByMapId(MapId);

        // --- 核心计算：真正的 RoomID ---
        // 直接拼接，不需要去查随机池子（除非你想做随机房间，那是另一回事）
        // 对于 SiteID=1 (起点)，通常是例外，可能直接用 100/201 等简单ID，
        // 但大部分中间房间都遵循 Prefix * 100 + SiteID
        
        // 尝试直接计算 ID
        int calculatedRoomId = roomPrefix * 100 + SiteId;

        // 验证一下这个 ID 是否存在于 RogueRoom 表中
        if (GameData.RogueRoomData.ContainsKey(calculatedRoomId))
        {
            RoomId = calculatedRoomId;
        }
        else
        {
            // 如果计算出来的 ID 不存在 (比如起点的 ID 比较特殊)
            // 尝试使用 fallback 逻辑或查找 RogueMapGenData
            if (GameData.RogueMapGenData.TryGetValue(SiteId, out var genData))
            {
                RoomId = genData.RandomElement();
            }
            else
            {
                // 实在找不到，回退到一个默认战斗房，防止崩服
                RoomId = 100; 
            }
        }

        // --- 加载详细配置 ---
        if (GameData.RogueRoomData.TryGetValue(RoomId, out var roomExcel))
        {
            Excel = roomExcel;
        }
        else
        {
            // 最终兜底
            Excel = GameData.RogueRoomData.Values.First(x => x.RogueRoomType == 1);
            RoomId = Excel.RogueRoomID;
        }

        // --- 等级计算 ---
        int progress = areaConfig.AreaProgress;
        int difficulty = areaConfig.Difficulty;
        this.MonsterLevel = progress * 10 + (difficulty - 1) * 10 + 5;
    }

    public int RoomId { get; set; }
    public int SiteId { get; set; }
    public RogueRoomStatus Status { get; set; } = RogueRoomStatus.Lock;
    public List<int> NextSiteIds { get; set; }
    public RogueRoomExcel Excel { get; set; }

    /// <summary>
    /// 【核心修改】根据 MapID 获取房间 ID 前缀
    /// 这个映射表是根据 RogueRoom.json 分析出来的
    /// </summary>
    private int GetRoomPrefixByMapId(int mapId)
    {
        return mapId switch
        {
            1 => 1,      // 世界 1 (简单) -> Room 100
            2 => 2,      // 世界 2 (简单) -> Room 201
            3 => 3,      // 世界 3 (雅利洛) -> Room 301
            101 => 3,    // 世界 3 变体 -> Room 301 (复用)
            
            200 => 1111, // 世界 4 (史瓦罗) -> Room 111121
            201 => 1111, // 世界 4 变体
            
            300 => 1211, // 世界 5 (卡芙卡) -> Room 121121
            301 => 1211,
            
            401 => 1311, // 世界 6 (可可利亚) -> Room 131121
            
            501 => 2001, // 世界 7 (玄鹿) -> Room 200121
            
            601 => 2111, // 世界 8 (彦卿) -> Room 211121
            
            701 => 3001, // 世界 9 (萨姆) -> Room 300121
            
            _ => 1 // 默认
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
