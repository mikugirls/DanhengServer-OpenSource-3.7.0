using EggLink.DanhengServer.GameServer.Server.Packet.Send.Gacha;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Gacha;

[Opcode(CmdIds.GetGachaCeilingCsReq)]
public class HandlerGetGachaCeilingCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        // 1. 解析客户端发来的请求
        var req = GetGachaCeilingCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;

        // 2. 构造响应消息体
        var rsp = new GetGachaCeilingScRsp
        {
            GachaType = req.GachaType, // 原样返回请求中的卡池类型 (通常是 1001)
            Retcode = 0,
            GachaCeiling = new GachaCeiling
            {
                // CeilingNum: 玩家当前的累计抽数（例如 150/300）
                CeilingNum = (uint)player.GachaManager.GachaData.StandardCumulativeCount,
                
                // IsClaimed: 玩家是否已经领取过这 300 抽的自选奖励
                IsClaimed = player.GachaManager.GachaData.IsStandardSelected,
                
                // AvatarList: 300 抽可选角色的列表
                // 通常包含：姬子(1003), 瓦尔特(1004), 布洛妮娅(1101), 杰帕德(1104), 克拉拉(1102), 彦卿(1209), 白露(1211)
                AvatarList = { 
                    new GachaCeilingAvatar { AvatarId = 1003 },
                    new GachaCeilingAvatar { AvatarId = 1004 },
                    new GachaCeilingAvatar { AvatarId = 1101 },
                    new GachaCeilingAvatar { AvatarId = 1102 },
                    new GachaCeilingAvatar { AvatarId = 1104 },
                    new GachaCeilingAvatar { AvatarId = 1209 },
                    new GachaCeilingAvatar { AvatarId = 1211 }
                }
            }
        };

        // 3. 发送响应包回客户端
        await connection.SendPacket(new PacketGetGachaCeilingScRsp(rsp));
    }
}
