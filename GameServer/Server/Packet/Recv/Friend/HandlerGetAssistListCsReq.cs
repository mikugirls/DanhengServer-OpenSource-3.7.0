using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Database.Player;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Friend;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.Friend;

[Opcode(CmdIds.GetAssistListCsReq)]
public class HandlerGetAssistListCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        if (connection?.Player?.Data == null) return;

        var rsp = new GetAssistListScRsp { Retcode = 0 };

        // 1. 获取所有有助战设置的玩家 ID（排除自己）
        // 关键修复：先 ToList() 到内存，绕过 SqlSugar 对 JSON 字段 Count 的解析限制
        var rawAssistData = DatabaseHelper.sqlSugarScope?
            .Queryable<AvatarData>()
            .Where(it => it.Uid != connection.Player.Uid)
            .ToList(); // 先拉取数据

        // 2. 在内存中过滤掉助战列表为空的玩家，并随机排序取前 10 条
        var assistDataList = rawAssistData?
            .Where(it => it.AssistAvatars != null && it.AssistAvatars.Count > 0)
            .OrderBy(_ => Guid.NewGuid()) // 内存中随机排序
            .Take(10)
            .ToList();

        if (assistDataList != null)
        {
            foreach (var avatarData in assistDataList)
            {
                var ownerData = PlayerData.GetPlayerByUid(avatarData.Uid);
                if (ownerData == null) continue;

                // 3. 随机选一个助战角色
                var avatarId = avatarData.AssistAvatars.RandomElement();
                if (avatarId == 0) continue;

                var avatarInfo = avatarData.FormalAvatars.FirstOrDefault(a => a.BaseAvatarId == avatarId);
                if (avatarInfo == null) continue;

                // 4. 构造响应信息
                var playerAssist = new PlayerAssistInfo
                {
                    PlayerInfo = ownerData.ToSimpleProto(FriendOnlineStatus.Offline)
                };

                // 获取大佬的真实仓库数据（如果可能），否则用 Mock
                var inventory = DatabaseHelper.Instance!.GetInstanceOrCreateNew<InventoryData>((int)ownerData.Uid);

                var mockCollection = new PlayerDataCollection(
                    ownerData,
                    inventory,
                    new EggLink.DanhengServer.Database.Lineup.LineupInfo()
                );

                // 转换协议，使用 3.7.0 的全参数签名
                var detail = avatarInfo.ToDetailProto(0, mockCollection);

                // 5. 等级平衡逻辑
                int myWorldLevel = connection.Player.Data.WorldLevel;
                if (GameData.AvatarPromotionConfigData.TryGetValue(avatarInfo.BaseAvatarId * 10 + myWorldLevel, out var config))
                {
                    if (detail.Level > (uint)config.MaxLevel)
                    {
                        detail.Level = (uint)config.MaxLevel;
                        detail.Promotion = (uint)myWorldLevel; 
                    }
                }

                playerAssist.MDHFANLHNMA = detail;
                rsp.AssistList.Add(playerAssist);
            }
        }

        await connection.SendPacket(new PacketGetAssistListScRsp(rsp));
    }
}