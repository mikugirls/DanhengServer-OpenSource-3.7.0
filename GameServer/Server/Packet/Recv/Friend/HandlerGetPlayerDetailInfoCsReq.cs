using EggLink.DanhengServer.GameServer.Server.Packet.Send.Friend;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Database;              // 提供 DatabaseHelper
using EggLink.DanhengServer.Database.Player;       // 提供 PlayerData
using EggLink.DanhengServer.Database.Avatar;       // 提供 AvatarData 和 PlayerDataCollection
using EggLink.DanhengServer.Database.Inventory;    // 提供 InventoryData

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Friend;
[Opcode(CmdIds.GetPlayerDetailInfoCsReq)]
public class HandlerGetPlayerDetailInfoCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = GetPlayerDetailInfoCsReq.Parser.ParseFrom(data);
        int targetUid = (int)req.Uid;

        // 1. 加载目标玩家的三大核心数据
        var playerData = PlayerData.GetPlayerByUid(targetUid);
        var targetAvatars = DatabaseHelper.Instance!.GetInstance<AvatarData>(targetUid);
        var targetInventory = DatabaseHelper.Instance!.GetInstance<InventoryData>(targetUid);

        if (playerData == null || targetAvatars == null || targetInventory == null)
        {
            await connection.SendPacket(new PacketGetPlayerDetailInfoScRsp());
            return;
        }

        // 2. 准备数据容器
        // 这里的 null! 告诉编译器：我知道这是 null，请无视它
		var collection = new PlayerDataCollection(playerData, targetInventory, null!);
        var detailInfo = playerData.ToDetailProto(); // 获取基础面板信息
        var displayList = new List<DisplayAvatarDetailInfo>();

        // 3. 核心修复：遍历展示角色，调用你写好的“满血版”ToDetailProto
        var pos = 0;
        foreach (var avatarId in targetAvatars.DisplayAvatars)
        {
            var formalAvatar = targetAvatars.FormalAvatars.Find(a => a.BaseAvatarId == avatarId);
            if (formalAvatar != null)
            {
                // 这里会调用你之前修复的、包含 Tid/MainAffixId/SubAffixList 的方法
                displayList.Add(formalAvatar.ToDetailProto(pos++, collection));
            }
        }

        // 4. 发送完整的包
        await connection.SendPacket(new PacketGetPlayerDetailInfoScRsp(detailInfo, displayList));
    }
}
