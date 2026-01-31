using EggLink.DanhengServer.GameServer.Server;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Activity;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Activity;


[Opcode(CmdIds.TakeLoginActivityRewardCsReq)]
public class HandlerTakeLoginActivityRewardCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = TakeLoginActivityRewardCsReq.Parser.ParseFrom(data);
        var player = connection.Player;

        if (player?.ActivityManager == null) return;

        // 1. 调用逻辑层
        var (rewardProto, panelId, retcode, _) = await player.ActivityManager.TakeLoginReward(req.Id, req.TakeDays);

        // 2. 【核心修复】：必须传回客户端请求的原始 ID (req.Id)
        // 哪怕服务器内部用 10018 存数据，发包必须发 1001801
        await connection.SendPacket(new PacketTakeLoginActivityRewardScRsp(
            req.Id,        // 这里改回 req.Id
            req.TakeDays, 
            retcode, 
            rewardProto, 
            panelId
        ));
		// 3. 【核心修正】：如果领取成功，立即推送最新的活动全量信息
        // 这会更新客户端内存里的 JLHOGGDHMHG 列表，让图标变灰
        if (retcode == 0)
        {
            var freshData = player.ActivityManager.GetLoginInfo();
            await connection.SendPacket(new PacketGetLoginActivityScRsp(freshData));
        }
    }
}