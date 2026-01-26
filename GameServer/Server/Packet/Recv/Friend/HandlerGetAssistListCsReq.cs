using EggLink.DanhengServer.GameServer.Server.Packet.Send.Friend;
using EggLink.DanhengServer.Kcp;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Friend;

[Opcode(CmdIds.GetAssistListCsReq)]
public class HandlerGetAssistListCsReq : Handler
{
   public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
{
    var rsp = new GetAssistListScRsp();
    
    // 1. 获取所有连接中的活跃玩家（在线助战）
    foreach (var kcp in DanhengListener.Connections.Values)
    {
        if (kcp is not Connection conn || conn.Player == null) continue;
        if (conn.Player.Uid == connection.Player.Uid) continue; // 不显示自己

        // 2. 检查对方是否设置了助战角色 (对应 AvatarData 中的 AssistAvatars 列表)
        var assistAvatars = conn.Player.AvatarManager.AvatarData.AssistAvatars;
        if (assistAvatars == null || assistAvatars.Count == 0) continue;

        foreach (var avatarId in assistAvatars)
        {
            var avatarInfo = conn.Player.AvatarManager.GetFormalAvatar(avatarId);
            if (avatarInfo == null) continue;

            // 3. 构建助战简要信息并应用【好友助战修正】
            var info = new AssistSimpleInfo
            {
                AvatarId = (uint)avatarInfo.AvatarId,
                Pos = 1,
                DressedSkinId = (uint)avatarInfo.GetCurPathInfo().Skin,
                
                // --- 等级修正逻辑 ---
                // 确保助战角色等级不会超过借用者（你）当前等级的 +10 级，防止数值碾压
                Level = (uint)Math.Min(avatarInfo.Level, connection.Player.Level + 10)
            };

            rsp.AssistSimpleInfoList.Add(info);
        }
    }

    // 4. 发送填充了数据的响应包
    await connection.SendPacket(new PacketGetAssistListScRsp(rsp));
}
}
