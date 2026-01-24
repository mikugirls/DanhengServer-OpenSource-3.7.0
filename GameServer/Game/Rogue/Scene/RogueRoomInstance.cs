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

    /// <summary>
    /// 初始化模拟宇宙房间 - 纯配置驱动版
    /// </summary>
    public RogueRoomInstance(RogueMapExcel excel, RogueAreaConfigExcel areaConfig)
    {
        SiteId = excel.SiteID;
        NextSiteIds = excel.NextSiteIDList;
        Status = excel.IsStart ? RogueRoomStatus.Unlock : RogueRoomStatus.Lock;

        // --- 1. 定位目标世界 BOSS (锚点) ---
        int worldIndex = (areaConfig.RogueAreaID / 10) % 10; 
        int targetBossId = GetBossRoomIdByWorldIndex(worldIndex);

        // --- 2. 核心：物理分组逻辑 (完全不使用 ID 前缀) ---
        // 直接从全量 JSON 列表中按顺序提取“属于当前世界的所有房源”
        var allRooms = GameData.RogueRoomData.Values.ToList();
        int bossIndex = allRooms.FindIndex(r => r.RogueRoomID == targetBossId);
        
        var worldRoomPool = new List<RogueRoomExcel>();
        if (bossIndex != -1)
        {
            // 从当前 BOSS 索引开始往前遍历，直到遇到上一个 Type 7 (BOSS)
            for (int i = bossIndex; i >= 0; i--)
            {
                var room = allRooms[i];
                // 如果遇到另一个 BOSS 且不是目标 BOSS 自己，说明跨界了，停止收集
                if (room.RogueRoomType == 7 && room.RogueRoomID != targetBossId)
                    break;
                
                worldRoomPool.Add(room);
            }
        }

       // --- 3. 动态站点职能判断 (优化版) ---
        bool isFinalBossSite = excel.NextSiteIDList == null || excel.NextSiteIDList.Count == 0;
        bool isEliteSite = (SiteId == 4 || SiteId == 8);
        // 扩展休息位判定
        bool isRespiteSite = (SiteId == 5 || SiteId == 9 || SiteId == 111 || SiteId == 112);

        if (isFinalBossSite)
        {
            RoomId = targetBossId;
        }
        else if (isEliteSite)
        {
            var elitePool = worldRoomPool.Where(r => r.RogueRoomType == 6).ToList();
            RoomId = elitePool.Count > 0 ? elitePool.RandomElement().RogueRoomID : GetDefaultRandom(SiteId);
        }
        else if (isRespiteSite)
        {
            // 休息位：在商店(5)和遭遇/事件(4/3)中随机
            var respitePool = worldRoomPool.Where(r => r.RogueRoomType == 5 || r.RogueRoomType == 4).ToList();
            RoomId = respitePool.Count > 0 ? respitePool.RandomElement().RogueRoomID : GetDefaultRandom(SiteId);
        }
        else
        {
           int roll = Random.Shared.Next(0, 100);
		int targetType = roll switch
		{
		< 40 => 1, // 40% 普通战斗
		< 60 => 2, // 20% 普通精英房（也就是你说的 2）
		< 85 => 3, // 25% 事件
		_    => 4  // 15% 休息/整备
		};

            var pool = worldRoomPool.Where(r => r.RogueRoomType == targetType).ToList();
            if (pool.Count == 0) pool = worldRoomPool.Where(r => r.RogueRoomType == 1).ToList();

            RoomId = pool.Count > 0 ? pool.RandomElement().RogueRoomID : GetDefaultRandom(SiteId);
        }

        // --- 4. 关键：场景同步与加载 ---
        if (GameData.RogueRoomData.TryGetValue(RoomId, out var roomExcel))
        {
            Excel = roomExcel;
            // 必须同步 MapEntrance (场景ID)，否则会进入“全量房”空壳
            // 世界 3 对应的 MapEntrance 是 8111101
            this.MapId = roomExcel.MapEntrance;
        }
        else
        {
            // 终极兜底
            Excel = GameData.RogueRoomData.Values.First(x => x.RogueRoomType == 1);
            RoomId = Excel.RogueRoomID;
            this.MapId = Excel.MapEntrance;
        }

        // --- 5. 等级计算 ---
        int baseLevel = areaConfig.RecommendLevel != 0 ? areaConfig.RecommendLevel : areaConfig.Difficulty * 10;
        this.MonsterLevel = baseLevel + (SiteId > 1 ? SiteId / 2 : 0);
    }

    private int GetBossRoomIdByWorldIndex(int index)
    {
        return index switch
        {
            1 => 307,
            2 => 200713,
            3 => 300713, // 世界 3 (杰帕德)
            4 => 111713,
            5 => 121713,
            6 => 122713,
            7 => 222713,
            8 => 231713,
            9 => 311713,
            _ => 111713
        };
    }

    private int GetDefaultRandom(int siteId)
    {
        return GameData.RogueMapGenData.TryGetValue(siteId, out var genData) 
                ? genData.RandomElement() : siteId;
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