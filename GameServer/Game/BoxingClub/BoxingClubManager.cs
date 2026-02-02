using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;
namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubManager(PlayerInstance player) : BasePlayerManager(player)
{
    // 用于记录当前正在进行的匹配信息，供进战斗使用
    public uint LastMatchGroupId { get; set; } = 0;
    public List<uint> LastMatchAvatars { get; set; } = new();

    /// <summary>
    /// 获取挑战列表协议数据重构
    /// 混淆类 FCIHIJLOMGA 字段业务映射说明:
    /// Tag 2  (ChallengeId) : 挑战关卡唯一 ID (1-5)
    /// Tag 15 (HJMGLEMJHKG) : 阶段分组 ID (StageGroupID)，决定了 UI 怪物弱点显示
    /// Tag 10 (APLKNJEGBKF) : 是否已通关 (IsFinished)，决定关卡是否显示“完成”图标
    /// Tag 13 (CPGOIPICPJF) : 历史最快回合数 (BestScore)，决定 S/A/B 评价级别
    /// Tag 9  (NAALCBMBPGC) : 当前进行的轮数/总回合累加 (TotalUsedTurns)
    /// Tag 8  (LLFOFPNDAFG) : 状态位 (Status)，1 为已开启/解锁
    /// Tag ?  (MDLACHDKMPH) : 该关卡限定的特殊试用角色池 (Special Avatars)
    /// Tag ?  (AvatarList)  : 记忆阵容，上次该关卡选中的角色 ID
    /// </summary>
    public List<FCIHIJLOMGA> GetChallengeList()
    {
        var challengeInfos = new List<FCIHIJLOMGA>();

        foreach (var config in GameData.BoxingClubChallengeData.Values)
        {
            // 1. 基础关卡状态构造
            var info = new FCIHIJLOMGA
            {
                ChallengeId = (uint)config.ChallengeID,
                HJMGLEMJHKG = (uint)config.StageGroupID, // 必填：否则怪物弱点界面不显示
                APLKNJEGBKF = false, // TODO: 从数据库读取 bool (HasFinished)
                CPGOIPICPJF = 0,     // 历史最快回合数：默认0表示无记录
                NAALCBMBPGC = 0,     // 活动进度回合数
                LLFOFPNDAFG = 1,     // 开启状态
                HNPEAPPMGAA = 0      // 预留占位
            };

            // 2. 注入限定试用角色 (解决“离队/看不到人”的关键)
            // 必须把 SpecialAvatarIDList 映射到混淆列表 MDLACHDKMPH
            if (config.SpecialAvatarIDList != null)
            {
                foreach (var trialId in config.SpecialAvatarIDList)
                {
                    info.MDLACHDKMPH.Add(new IJKJJDHLKLB
                    {
                        AvatarId = (uint)trialId,
                        // 拳击俱乐部限定角色通常使用 AvatarLimitType(2)
                        AvatarType = AvatarType.AvatarLimitType 
                    });
                }
            }

            // 3. 记忆阵容回显 (可选逻辑)
            // 如果玩家上次打过该关卡且保存了阵容，客户端会自动勾选这几个人
            if (LastMatchAvatars.Count > 0 && LastMatchGroupId == (uint)config.ChallengeID)
            {
                info.AvatarList.AddRange(LastMatchAvatars);
            }

            challengeInfos.Add(info);
        }

        return challengeInfos;
    }
}

    

