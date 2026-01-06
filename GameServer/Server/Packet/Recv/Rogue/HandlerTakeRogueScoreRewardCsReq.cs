using EggLink.DanhengServer.GameServer.Server.Packet.Send.Rogue;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Rogue;

// 绑定领奖请求的 Opcode
[Opcode(CmdIds.TakeRogueScoreRewardCsReq)]
public class HandlerTakeRogueScoreRewardCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        // 1. 解析客户端发来的请求数据
        var req = TakeRogueScoreRewardCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;

        // 2. 调用 RogueManager 处理领奖逻辑
        if (player.RogueManager != null)
        {
            await player.RogueManager.HandleTakeRogueScoreReward(req);
        }
    }
}