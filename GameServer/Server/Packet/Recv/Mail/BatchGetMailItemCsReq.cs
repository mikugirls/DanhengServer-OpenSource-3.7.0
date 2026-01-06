using EggLink.DanhengServer.GameServer.Server.Packet.Send.Mail;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using System.Linq;
using System.Threading.Tasks;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Mail;

// 修改点：使用 TakeMailAttachmentCsReq (ID 894)
[Opcode(CmdIds.TakeMailAttachmentCsReq)]
public class HandlerBatchGetMailItemCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
{
    // 1. 解析协议数据
    var req = TakeMailAttachmentCsReq.Parser.ParseFrom(data);
    
    // 2. 严格的空值检查：确保请求、邮件列表、玩家以及邮件管理器都不为空
    if (req == null || req.MailIdList == null || req.MailIdList.Count == 0) return;
    
    var player = connection.Player;
    if (player?.MailManager == null) return;

    // 3. 正确的异步等待方式：不要在 ValueTask 上使用 ?.
    await player.MailManager.TakeMailAttachments(req.MailIdList.ToList());
}
}