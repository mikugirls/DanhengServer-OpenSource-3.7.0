using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;

namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubManager(PlayerInstance player) : BasePlayerManager(player)
{
	public uint LastMatchGroupId { get; set; } = 0;
    public List<uint> LastMatchAvatars { get; set; } = new();
    /// <summary>
    /// 获取所有挑战等级的详细进度，数据来源于 BoxingClubChallenge.json
    /// </summary>
 public List<FCIHIJLOMGA> GetChallengeList()
{
    var challengeInfos = new List<FCIHIJLOMGA>();

    // 筛选配置表，只处理 ChallengeID 在 1 到 5 之间的关卡
    foreach (var config in GameData.BoxingClubChallengeData.Values)
    {
        //if (config.ChallengeID < 1 || config.ChallengeID > 5) 
        //    continue; 

        var info = new FCIHIJLOMGA
        {
            // Tag 2: 挑战 ID (羽量级到重量级)
            ChallengeId = (uint)config.ChallengeID, 
            
           // 这个是分组（这个对了怪物都显示了）
            //HJMGLEMJHKG = 10,
            
            // Tag 10: APLKNJEGBKF -> 是否已通关
            APLKNJEGBKF = false, 
            
			// Tag 9: NAALCBMBPGC -> 总轮数
            NAALCBMBPGC = 0,

            // Tag 8: LLFOFPNDAFG -> 状态位 (开启)
            LLFOFPNDAFG = 1,

            // Tag 14: HNPEAPPMGAA -> 不清楚
            HNPEAPPMGAA = 0,

            // Tag 13: CPGOIPICPJF -> 排序
            //CPGOIPICPJF = (uint)config.ChallengeID
        };
		// 2. 填充试用阵容 (MDLACHDKMPH)
        if (config.SpecialAvatarIDList != null)
        {
            foreach (var trialId in config.SpecialAvatarIDList)
            {
                info.MDLACHDKMPH.Add(new IJKJJDHLKLB
                {
                    AvatarId = trialId, 
                    AvatarType = AvatarType.AvatarTrialType 
                });
            }
        }
		challengeInfos.Add(info);
       // 3. 【解决你的问题】同步上次使用的队伍
        // 如果你需要显示“上次队伍”，需要在这里填充 AvatarList
        if (LastMatchAvatars.Count > 0 && LastMatchGroupId == (uint)config.ChallengeID)
        {
            foreach (var id in LastMatchAvatars)
            {
                // 注意：这里需要根据你的 Proto 结构添加，通常是 uint 列表
                info.AvatarList.Add(id); 
            }
        }
     }
		

        
		return challengeInfos;
    }

    
}
