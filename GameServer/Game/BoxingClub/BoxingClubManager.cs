using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;
namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubManager(PlayerInstance player) : BasePlayerManager(player)
{
    // 核心进度：当前匹配到的对手索引 (1-based Index，对应你说的“第几个怪”)
    public uint CurrentOpponentIndex { get; set; } = 0;
    
    // 进度状态：当前参与的关卡 ID (1-5)
    public uint CurrentChallengeId { get; set; } = 0;

    // 记忆阵容：解决选人界面角色“离队”消失的核心存储
    public List<uint> LastMatchAvatars { get; set; } = new();

    /// <summary>
    /// 获取挑战列表协议数据重构
    /// 混淆类 FCIHIJLOMGA 字段业务映射说明:
    /// Tag 2  (ChallengeId)  : 关卡 ID (羽量级/重量级等)。
    /// Tag 15 (HJMGLEMJHKG)  : 【关键】对手位置索引 (1-based)。填 1 就是第一个怪，填 10 就是第十个。
    /// Tag 10 (APLKNJEGBKF)  : 是否已通关 (IsFinished)。
    /// Tag 13 (CPGOIPICPJF)  : 历史最快回合数 (MinRounds)，决定评价等级。
    /// Tag 9  (NAALCBMBPGC)  : 当前挑战实时累计回合数 (TotalUsedTurns)。
    /// Tag 8  (LLFOFPNDAFG)  : 开启状态 (Status)，填 1 解锁。
    /// Tag 11 (MDLACHDKMPH)  : 限定试用角色池 (SpecialAvatarList)，解决选人界面无头像。
    /// Tag 6  (AvatarList)   : 选人记忆列表 (SelectedAvatars)，解决已选角色离队。
    /// </summary>
    public List<FCIHIJLOMGA> GetChallengeList()
    {
        var challengeInfos = new List<FCIHIJLOMGA>();

        foreach (var config in GameData.BoxingClubChallengeData.Values)
        {
            var info = new FCIHIJLOMGA
            {
                ChallengeId = (uint)config.ChallengeID,
                LLFOFPNDAFG = 1,     // 默认开启
                APLKNJEGBKF = false, // TODO: 数据库读取完成状态
                CPGOIPICPJF = 0,     // 历史最高评分回合
                NAALCBMBPGC = 0,     // 当前进度回合
            };

            // 1. 注入试用角色池 (让选人界面能看到那些带“试”字的角色)
            if (config.SpecialAvatarIDList != null)
            {
                foreach (var trialId in config.SpecialAvatarIDList)
                {
                    info.MDLACHDKMPH.Add(new IJKJJDHLKLB
                    {
                        AvatarId = (uint)trialId,
                        AvatarType = AvatarType.AvatarLimitType // 必须填 2
                    });
                }
            }

            // 2. 处理“正在进行中”的匹配状态
            if (CurrentChallengeId == (uint)config.ChallengeID)
            {
                // 如果你通过随机算法生成了 Index，这里填入 1-9，就不会永远是第十个了
                info.HJMGLEMJHKG = CurrentOpponentIndex; 
                
                // 【核心修复】将选中的 4 个英雄 ID 塞回列表，UI 才不会显示“离队”
                if (LastMatchAvatars.Count > 0)
                {
                    info.AvatarList.AddRange(LastMatchAvatars);
                }
            }

            challengeInfos.Add(info);
        }

        return challengeInfos;
    }
    /// <summary>
    /// 处理匹配请求并构造回显快照
    /// 混淆字段映射：
    /// HJMGLEMJHKG (Tag 15): 匹配结果索引 (1-based)
    /// MDLACHDKMPH (Tag 11): 选中的试用角色列表 (IJKJJDHLKLB)
    /// AvatarList  (Tag 6) : 选中的全员 ID 列表
    /// </summary>
    public FCIHIJLOMGA ProcessMatchRequest(MatchBoxingClubOpponentCsReq req)
    {
        // 1. 执行随机逻辑：产生 1-10 的随机索引，打破“永远第十个怪”
        uint selectedIndex = (uint)new Random().Next(1, 11);

        // 2. 持久化数据到 Manager，供后续 StartBattle 提取
        this.LastChallengeId = req.ChallengeId;
        this.LastMatchOpponentIndex = selectedIndex;
        this.LastMatchAvatars = req.AvatarList.ToList();

        // 3. 构造回显快照 (Snapshot)
        var snapshot = new FCIHIJLOMGA
        {
            ChallengeId = req.ChallengeId,
            HJMGLEMJHKG = selectedIndex, // 下发随机结果
            NAALCBMBPGC = 0,             // 初始回合数
            APLKNJEGBKF = false,         // 通关状态
            LLFOFPNDAFG = 1,             // 激活状态
            HNPEAPPMGAA = 0
        };

        // 4. 【核心修复】直接镜像传回客户端发送的试用角色列表
        // 客户端发什么，我们就填什么传回去，这样选人界面才不会“离队”
        snapshot.MDLACHDKMPH.AddRange(req.MDLACHDKMPH);

        // 5. 【核心修复】直接镜像传回客户端发送的全员阵容列表
        snapshot.AvatarList.AddRange(req.AvatarList);

        return snapshot;
    }
}

    

