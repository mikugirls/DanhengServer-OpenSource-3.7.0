using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using System.Collections.Generic;
using System.Linq;

namespace EggLink.DanhengServer.GameServer.Game.Rogue.Scene;

public class RogueRoomInstance
{
    public int RoomId { get; set; }
    public int SiteId { get; set; }
    public RogueRoomStatus Status { get; set; } = RogueRoomStatus.Lock;
    public List<int> NextSiteIds { get; set; }
    public RogueRoomExcel Excel { get; set; } 
    public int MonsterLevel { get; set; } 
    public int MapId { get; set; }

    // 【修改点】：增加 usedRoomIds 参数用于在同一个地图周期内去重
    public RogueRoomInstance(RogueMapExcel excel, RogueAreaConfigExcel areaConfig, int depth, HashSet<int> usedRoomIds)
    {
        SiteId = excel.SiteID;
        NextSiteIds = excel.NextSiteIDList;
        Status = excel.IsStart ? RogueRoomStatus.Unlock : RogueRoomStatus.Lock;

        int worldIndex = (areaConfig.RogueAreaID / 10) % 10; 
        int targetBossId = GetBossRoomIdByWorldIndex(worldIndex);

        var allRooms = GameData.RogueRoomData.Values.ToList();
        int bossIndex = allRooms.FindIndex(r => r.RogueRoomID == targetBossId);
        var worldRoomPool = new List<RogueRoomExcel>();
        
        RogueRoomExcel? tempExcel = null;

        if (bossIndex != -1)
        {
            int targetMapEntrance = allRooms[bossIndex].MapEntrance;

            for (int i = bossIndex; i >= 0; i--)
            {
                var room = allRooms[i];
                
                // 统一过滤：BUG房间和非法ID
                if (IsBuggyRoom(room)) continue;

                if (room.RogueRoomType == 7 && room.RogueRoomID != targetBossId) break;
                
                if (room.MapEntrance != targetMapEntrance) continue;

                worldRoomPool.Add(room);
            }
        }

        // 职能判定
        bool isFinalBossSite = excel.NextSiteIDList == null || excel.NextSiteIDList.Count == 0;
        int targetType;

        if (isFinalBossSite)
        {
            targetType = 7;
            RoomId = targetBossId;
        }
        else if (depth == 4 || depth == 8)
        {
            targetType = 6; // 精英
        }
        else if (depth == 5 || depth == 9 || depth == 12)
        {
            targetType = 5; // 休息
        }
        else
        {
            int roll = System.Random.Shared.Next(0, 100);
            targetType = roll switch { < 70 => 1, < 95 => 3, _ => 4 };
        }

        // --- 房源抽取逻辑优化：实现“尽可能不重复” ---
        if (targetType != 7)
        {
            // 优先选：类型匹配 且 还没被用过 的房间
            var finalPool = worldRoomPool
                .Where(r => r.RogueRoomType == targetType && !usedRoomIds.Contains(r.RogueRoomID))
                .ToList();
            
            // 如果没用过的抽完了（比如池子只有2个精英房，但地图有3个精英位），则允许重复
            if (finalPool.Count == 0) 
            {
                finalPool = worldRoomPool.Where(r => r.RogueRoomType == targetType).ToList();
            }

            // 基础兜底
            if (finalPool.Count == 0) 
                finalPool = worldRoomPool.Where(r => r.RogueRoomType == 1).ToList();

            // 随机选择
            RoomId = finalPool.Count > 0 ? finalPool.RandomElement().RogueRoomID : targetBossId;
            
            // 【关键】：将选中的ID记录到去重篮子里
            usedRoomIds.Add(RoomId);
        }

        if (GameData.RogueRoomData.TryGetValue(RoomId, out var roomExcel))
        {
            tempExcel = roomExcel;
        }
        else
        {
            tempExcel = allRooms[bossIndex]; 
        }

        this.Excel = tempExcel;
        this.MapId = tempExcel.MapEntrance;

        int baseLevel = areaConfig.RecommendLevel != 0 ? areaConfig.RecommendLevel : areaConfig.Difficulty * 10;
        this.MonsterLevel = baseLevel + (depth > 1 ? depth / 2 : 0);

        System.Console.WriteLine($"[Rogue生成验证] 站点:{SiteId} | 选中房间:{RoomId} | 类型:{targetType} | 是否已重复:{usedRoomIds.Count(x => x == RoomId) > 1}");
    }

    private static bool IsBuggyRoom(RogueRoomExcel room)
    {
        if (room == null) return true;
        if (room.RogueRoomID >= 1000000) return true;
        int[] bugIds = { 200242, 300312 };
        if (bugIds.Contains(room.RogueRoomID)) return true;
        if (room.GroupID == 0) return true;
        return false;
    }

    private int GetBossRoomIdByWorldIndex(int index)
    {
        return index switch
        {
            1 => 307,
            2 => 200713,
            3 => 111713,
            4 => 121713,
            5 => 122713,
            6 => 131713,
            7 => 222713,
            8 => 231713,
            9 => 311713,
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