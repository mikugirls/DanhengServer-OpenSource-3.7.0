using EggLink.DanhengServer.GameServer.Server.Packet.Send.Gacha;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Gacha;

[Opcode(CmdIds.ExchangeGachaCeilingCsReq)]
public class HandlerExchangeGachaCeilingCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = ExchangeGachaCeilingCsReq.Parser.ParseFrom(data);
        var player = connection.Player!;
        var gachaData = player.GachaManager!.GachaData!;

        // 1. 条件校验
        if (gachaData.StandardCumulativeCount < 300 || gachaData.IsStandardSelected)
        {
            await connection.SendPacket(new PacketExchangeGachaCeilingScRsp(3602));
            return;
        }

        // 2. 调用 InventoryManager 的 AddItem 方法
        // 既然你的 AddItem 已经写了 case ItemMainTypeEnum.AvatarCard:
        // 它会自动判断：没角色发角色，有角色发星魂(+10000)，并且你刚加了发头像(+20000)的逻辑
        var result = await player.InventoryManager!.AddItem((int)req.AvatarId, 1, notify: true, sync: true);

        if (result != null)
        {
            // 3. 兑换成功，标记状态并保存
            gachaData.IsStandardSelected = true;
            player.GachaManager.Save();

            // 4. 返回成功协议包
            var rsp = new ExchangeGachaCeilingScRsp
            {
                Retcode = 0,
                GachaType = req.GachaType,
                AvatarId = req.AvatarId
            };
            await connection.SendPacket(new PacketExchangeGachaCeilingScRsp(rsp));
        }
        else
        {
            // 如果 AddItem 返回 null (比如 ID 错误)，返回通用错误
            await connection.SendPacket(new PacketExchangeGachaCeilingScRsp(1)); 
        }
    }
}
