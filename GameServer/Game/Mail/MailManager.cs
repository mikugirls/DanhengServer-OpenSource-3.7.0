using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Database.Mail;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Mail;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Game.Mail;

public class MailManager(PlayerInstance player) : BasePlayerManager(player)
{
    public MailData MailData { get; } = DatabaseHelper.Instance!.GetInstanceOrCreateNew<MailData>(player.Uid);

    public List<MailInfo> GetMailList()
    {
        return MailData.MailList;
    }

    public MailInfo? GetMail(int mailId)
    {
        return MailData.MailList.Find(x => x.MailID == mailId);
    }

    public void DeleteMail(int mailId)
    {
        var index = MailData.MailList.FindIndex(x => x.MailID == mailId);
        MailData.MailList.RemoveAt(index);
    }

    public async ValueTask SendMail(string sender, string title, string content, int templateId, int expiredDay = 30)
    {
        var mail = new MailInfo
        {
            MailID = MailData.NextMailId++,
            SenderName = sender,
            Content = content,
            Title = title,
            TemplateID = templateId,
            SendTime = DateTime.Now.ToUnixSec(),
            ExpireTime = DateTime.Now.AddDays(expiredDay).ToUnixSec()
        };

        MailData.MailList.Add(mail);

        await Player.SendPacket(new PacketNewMailScNotify(mail.MailID));
    }

    public async ValueTask SendMail(string sender, string title, string content, int templateId,
        List<ItemData> attachments, int expiredDay = 30)
    {
        var mail = new MailInfo
        {
            MailID = MailData.NextMailId++,
            SenderName = sender,
            Content = content,
            Title = title,
            TemplateID = templateId,
            SendTime = DateTime.Now.ToUnixSec(),
            ExpireTime = DateTime.Now.AddDays(expiredDay).ToUnixSec(),
            Attachment = new MailAttachmentInfo
            {
                Items = attachments
            }
        };

        MailData.MailList.Add(mail);

        await Player.SendPacket(new PacketNewMailScNotify(mail.MailID));
    }
  public async ValueTask TakeMailAttachments(List<uint> mailIds)
{
    var totalRewards = new List<ItemData>();
    var successfulMailIds = new List<uint>();

    foreach (var id in mailIds)
    {
        var mail = GetMail((int)id);
        if (mail == null || mail.Attachment?.Items == null || mail.Attachment.Items.Count == 0) continue;

        totalRewards.AddRange(mail.Attachment.Items);
        successfulMailIds.Add(id);

        mail.Attachment.Items.Clear();
        mail.IsRead = true;
    }

    if (totalRewards.Count > 0)
    {
        // 1. 发放到背包
        if (Player.InventoryManager != null)
        {
            await Player.InventoryManager.AddItems(totalRewards);
        }
        
        // 2. 发送响应包
        await Player.SendPacket(new PacketBatchGetMailItemScRsp(successfulMailIds, totalRewards));
        
        // 3. 【核心修正】将当前玩家 UID 加入保存列表
        // 这样后台线程会在 5 分钟内自动完成数据库更新
        if (!DatabaseHelper.ToSaveUidList.Contains(Player.Uid))
        {
            DatabaseHelper.ToSaveUidList.Add(Player.Uid);
        }
    }
}
    public List<ClientMail> ToMailProto()
    {
        var list = new List<ClientMail>();

        foreach (var mail in MailData.MailList) list.Add(mail.ToProto());

        return list;
    }

    public List<ClientMail> ToNoticeMailProto()
    {
        var list = new List<ClientMail>();

        foreach (var mail in MailData.NoticeMailList) list.Add(mail.ToProto());

        return list;
    }
}