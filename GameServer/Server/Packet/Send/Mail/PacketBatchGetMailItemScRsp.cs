using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Database.Inventory;
using System.Collections.Generic;
using System.Linq;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Mail;

public class PacketBatchGetMailItemScRsp : BasePacket
{
    public PacketBatchGetMailItemScRsp(List<uint> mailIds, List<ItemData> items) : base(CmdIds.TakeMailAttachmentScRsp)
    {
        // 这里的 TakeMailAttachmentScRsp 必须匹配生成的源码字段
        var proto = new TakeMailAttachmentScRsp
        {
            Retcode = 0,
            // 修正：生成的源码中属性名为 SuccMailIdList
            SuccMailIdList = { mailIds } 
        };

        if (items != null && items.Count > 0)
        {
            var itemList = new ItemList();
            foreach (var item in items)
            {
                itemList.ItemList_.Add(item.ToProto());
            }
            // 修正：生成的源码中属性名为 Attachment
            proto.Attachment = itemList; 
        }

        SetData(proto);
    }
}