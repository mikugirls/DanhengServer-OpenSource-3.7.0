using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Data;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.BoxingClub;

[Opcode(CmdIds.MatchBoxingClubOpponentCsReq)]
public class HandlerMatchBoxingClubOpponentCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = MatchBoxingClubOpponentCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;

        // 1. 获取关卡配置
        if (!GameData.BoxingClubChallengeData.TryGetValue((int)req.ChallengeId, out var config))
        {
            await connection.SendPacket(new PacketMatchBoxingClubOpponentScRsp((uint)Retcode.RetBoxingClubChallengeNotOpen, null));
            return;
        }

        // 2. 抽选逻辑固定：取第一个怪物组
        int selectedGroupId = 10 ;

        // 3. 构造匹配成功的快照数据 (FCIHIJLOMGA)
        var challengeSnapshot = new FCIHIJLOMGA
        {
            // Tag 2: 挑战 ID
            ChallengeId = req.ChallengeId,
            
            // Tag 4: HJMGLEMJHKG -> 匹配到的怪物组结果
            HJMGLEMJHKG = (uint)selectedGroupId,
            
            // Tag 9: NAALCBMBPGC -> 累计轮数 (初始 0)
            NAALCBMBPGC = 0,
            
            // Tag 10: APLKNJEGBKF -> 是否已通关
            APLKNJEGBKF = false,
            
            // Tag 14: HNPEAPPMGAA -> 不清楚
            HNPEAPPMGAA = 0
        };
		// 1. 处理玩家选中的自选角色 (必须同时加入 Tag 6 和 Tag 3)
		// --- 核心修复：直接读取 MDLACHDKMPH 列表 ---
		foreach (var avatar in req.MDLACHDKMPH)
	{
    // 1. 同步回包快照，防止界面消失/转圈
    challengeSnapshot.MDLACHDKMPH.Add(new IJKJJDHLKLB
    {
        AvatarId = avatar.AvatarId,
        AvatarType = avatar.AvatarType 
    });

    // 2. 同样填充 AvatarList (Tag 3)
    challengeSnapshot.AvatarList.Add(avatar.AvatarId);
	}
		
		// 存下数据供进站使用
		player.BoxingClubManager.LastMatchGroupId = (uint)selectedGroupId;
		player.BoxingClubManager.LastMatchAvatars = req.AvatarList.ToList();

       

        // 5. 发送响应包
        await connection.SendPacket(new PacketMatchBoxingClubOpponentScRsp((uint)Retcode.RetSucc, challengeSnapshot));
    }
}