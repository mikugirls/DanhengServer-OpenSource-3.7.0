using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Database.Player;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Friend;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Friend;

[Opcode(CmdIds.GetAssistListCsReq)]
public class HandlerGetAssistListCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        if (connection?.Player?.Data == null) return;

        var rsp = new GetAssistListScRsp { Retcode = 0 };

        // 1. 从数据库读取有助战设置的离线玩家 (取前20个)
        var assistDataList = DatabaseHelper.sqlSugarScope?
            .Queryable<AvatarData>()
            .Where(it => it.AssistAvatars != null && it.AssistAvatars.Count > 0)
            .ToPageList(1, 20);

        if (assistDataList != null)
        {
            foreach (var avatarData in assistDataList)
            {
                if (avatarData.Uid == connection.Player.Uid) continue;

                // 获取玩家基础信息 (名字、头像等)
                var ownerData = PlayerData.GetPlayerByUid(avatarData.Uid);
                if (ownerData == null) continue;

                foreach (var avatarId in avatarData.AssistAvatars)
                {
                    var avatarInfo = avatarData.FormalAvatars.FirstOrDefault(a => a.AvatarId == avatarId);
                    if (avatarInfo == null) continue;

                    // --- 关键修正：必须构造 PlayerAssistInfo ---
                    var playerAssist = new PlayerAssistInfo
                    {
                        // 填充外层玩家信息
                        PlayerInfo = ownerData.ToSimpleProto(FriendOnlineStatus.Offline), 
                    };

                    // 填充内层角色简易信息
                    playerAssist.AssistInfo = new AssistSimpleInfo
                    {
                        AvatarId = (uint)avatarInfo.AvatarId,
                        Level = (uint)Math.Min(avatarInfo.Level, connection.Player.Data.Level + 10), // 好友助战修正
                        Pos = 1,
                        DressedSkinId = (uint)avatarInfo.GetCurPathInfo().Skin
                    };

                    // 添加到最终列表
                    rsp.AssistList.Add(playerAssist);
                }
            }
        }

        await connection.SendPacket(new PacketGetAssistListScRsp(rsp));
    }
}
