using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.Friend;
using EggLink.DanhengServer.Data; // 必须有这一行，才能找到 GameData
using EggLink.DanhengServer.Database.Player;
using EggLink.DanhengServer.GameServer.Command;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Server;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Chat;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Friend;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Game.Friend;

public class FriendManager(PlayerInstance player) : BasePlayerManager(player)
{   public static bool EnableRecommendLog = false; // LOG开关，默认关闭
    public FriendData FriendData { get; set; } =
        DatabaseHelper.Instance!.GetInstanceOrCreateNew<FriendData>(player.Uid);

    public async ValueTask<Retcode> AddFriend(int targetUid)
    {
        if (targetUid == Player.Uid) return Retcode.RetSucc; // Cannot add self
        if (FriendData.FriendDetailList.ContainsKey(targetUid)) return Retcode.RetFriendAlreadyIsFriend;
        if (FriendData.BlackList.Contains(targetUid)) return Retcode.RetFriendInBlacklist;
        if (FriendData.SendApplyList.Contains(targetUid)) return Retcode.RetSucc; // Already send apply

        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        if (target == null) return Retcode.RetFriendPlayerNotFound;
        if (target.BlackList.Contains(Player.Uid)) return Retcode.RetFriendInTargetBlacklist;
        if (target.ReceiveApplyList.Contains(targetUid)) return Retcode.RetSucc; // Already receive apply

        FriendData.SendApplyList.Add(targetUid);
        target.ReceiveApplyList.Add(Player.Uid);

        var targetPlayer = Listener.GetActiveConnection(targetUid);
        if (targetPlayer != null)
            await targetPlayer.SendPacket(new PacketSyncApplyFriendScNotify(Player.Data));

        DatabaseHelper.ToSaveUidList.Add(targetUid);
        return Retcode.RetSucc;
    }

    public async ValueTask<PlayerData?> ConfirmAddFriend(int targetUid)
    {
        if (targetUid == Player.Uid) return null; // Cannot add self
        if (FriendData.FriendDetailList.ContainsKey(targetUid)) return null;
        if (FriendData.BlackList.Contains(targetUid)) return null;

        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        var targetData = PlayerData.GetPlayerByUid(targetUid);
        if (target == null || targetData == null) return null;
        if (target.FriendDetailList.ContainsKey(Player.Uid)) return null;
        if (target.BlackList.Contains(Player.Uid)) return null;

        FriendData.ReceiveApplyList.Remove(targetUid);
        FriendData.FriendDetailList.Add(targetUid, new FriendDetailData());
        target.SendApplyList.Remove(Player.Uid);
        target.FriendDetailList.Add(Player.Uid, new FriendDetailData());

        var targetPlayer = Listener.GetActiveConnection(targetUid);
        if (targetPlayer != null)
            await targetPlayer.SendPacket(new PacketSyncHandleFriendScNotify((uint)Player.Uid, true, Player.Data));

        DatabaseHelper.ToSaveUidList.Add(targetUid);
        return targetData;
    }

    public async ValueTask RefuseAddFriend(int targetUid)
    {
        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        if (target == null) return;

        FriendData.ReceiveApplyList.Remove(targetUid);
        target.SendApplyList.Remove(Player.Uid);

        var targetPlayer = Listener.GetActiveConnection(targetUid);
        if (targetPlayer != null)
            await targetPlayer.SendPacket(new PacketSyncHandleFriendScNotify((uint)Player.Uid, false, Player.Data));

        DatabaseHelper.ToSaveUidList.Add(targetUid);
    }

    public async ValueTask<PlayerData?> AddBlackList(int targetUid)
    {
        var blackInfo = GetFriendPlayerData([targetUid]).First();
        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        if (blackInfo == null || target == null) return null;

        FriendData.FriendDetailList.Remove(targetUid);
        target.FriendDetailList.Remove(Player.Uid);
        if (!FriendData.BlackList.Contains(targetUid))
            FriendData.BlackList.Add(targetUid);

        var targetPlayer = Listener.GetActiveConnection(targetUid);
        if (targetPlayer != null)
            await targetPlayer.SendPacket(new PacketSyncAddBlacklistScNotify(Player.Uid));

        DatabaseHelper.ToSaveUidList.Add(targetUid);
        return blackInfo;
    }

    public void RemoveBlackList(int targetUid)
    {
        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        if (target == null) return;
        FriendData.BlackList.Remove(targetUid);
    }

    public async ValueTask<int?> RemoveFriend(int targetUid)
    {
        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        if (target == null) return null;

        FriendData.FriendDetailList.Remove(targetUid);
        target.FriendDetailList.Remove(Player.Uid);

        var targetPlayer = Listener.GetActiveConnection(targetUid);
        if (targetPlayer != null)
            await targetPlayer.SendPacket(new PacketSyncDeleteFriendScNotify(Player.Uid));

        DatabaseHelper.ToSaveUidList.Add(targetUid);
        return targetUid;
    }

    public async ValueTask SendMessage(int sendUid, int recvUid, string? message = null, int? extraId = null)
    {
        var data = new FriendChatData
        {
            SendUid = sendUid,
            ReceiveUid = recvUid,
            Message = message ?? "",
            ExtraId = extraId ?? 0,
            SendTime = Extensions.GetUnixSec()
        };

        if (!FriendData.ChatHistory.TryGetValue(recvUid, out var value))
        {
            FriendData.ChatHistory[recvUid] = new FriendChatHistory();
            value = FriendData.ChatHistory[recvUid];
        }

        value.MessageList.Add(data);

        PacketRevcMsgScNotify proto;
        if (message != null)
            proto = new PacketRevcMsgScNotify((uint)recvUid, (uint)sendUid, message);
        else
            proto = new PacketRevcMsgScNotify((uint)recvUid, (uint)sendUid, (uint)(extraId ?? 0));

        await Player.SendPacket(proto);

       // 判定：消息不为空，且以 "GM#" 开头
            if (message != null && message.StartsWith("GM#"))
            {
          // 截取 "GM#" 之后的所有内容作为指令
          // 例如输入 "GM#level 80"，截取出的 cmd 就是 "level 80"
              var cmd = message.Substring(3); 
    
          // 执行指令
           CommandExecutor.ExecuteCommand(new PlayerCommandSender(Player), cmd);
            }

        // receive message
        var recvPlayer = Listener.GetActiveConnection(recvUid)?.Player;
        if (recvPlayer != null)
        {
            await recvPlayer.FriendManager!.ReceiveMessage(sendUid, recvUid, message, extraId);
        }
        else
        {
            // offline
            var friendData = DatabaseHelper.Instance!.GetInstance<FriendData>(recvUid);
            if (friendData == null) return; // not exist maybe server profile
            if (!friendData.ChatHistory.TryGetValue(sendUid, out var history))
            {
                friendData.ChatHistory[sendUid] = new FriendChatHistory();
                history = friendData.ChatHistory[sendUid];
            }

            history.MessageList.Add(data);

            DatabaseHelper.ToSaveUidList.Add(recvUid);
        }
    }

    public async ValueTask SendInviteMessage(int sendUid, int recvUid, LobbyInviteInfo info)
    {
        var proto = new PacketRevcMsgScNotify((uint)recvUid, (uint)sendUid, info);
        await Player.SendPacket(proto);

        // receive message
        var recvPlayer = Listener.GetActiveConnection(recvUid)?.Player;
        if (recvPlayer != null) await recvPlayer.FriendManager!.ReceiveInviteMessage(sendUid, recvUid, info);
    }

    public async ValueTask ReceiveMessage(int sendUid, int recvUid, string? message = null, int? extraId = null)
    {
        var data = new FriendChatData
        {
            SendUid = sendUid,
            ReceiveUid = recvUid,
            Message = message ?? "",
            ExtraId = extraId ?? 0,
            SendTime = Extensions.GetUnixSec()
        };

        if (!FriendData.ChatHistory.TryGetValue(sendUid, out var value))
        {
            FriendData.ChatHistory[sendUid] = new FriendChatHistory();
            value = FriendData.ChatHistory[sendUid];
        }

        value.MessageList.Add(data);

        PacketRevcMsgScNotify proto;
        if (message != null)
            proto = new PacketRevcMsgScNotify((uint)recvUid, (uint)sendUid, message);
        else
            proto = new PacketRevcMsgScNotify((uint)recvUid, (uint)sendUid, (uint)(extraId ?? 0));

        await Player.SendPacket(proto);
    }

    public async ValueTask ReceiveInviteMessage(int sendUid, int recvUid, LobbyInviteInfo info)
    {
        var proto = new PacketRevcMsgScNotify((uint)recvUid, (uint)sendUid, info);

        await Player.SendPacket(proto);
    }

    public FriendDetailData? GetFriendDetailData(int uid)
    {
        if (uid == ConfigManager.Config.ServerOption.ServerProfile.Uid)
            return new FriendDetailData { IsMark = true };

        if (!FriendData.FriendDetailList.TryGetValue(uid, out var friend)) return null;

        return friend;
    }

    public List<ChatMessageData> GetHistoryInfo(int uid)
    {
        if (!FriendData.ChatHistory.TryGetValue(uid, out var history)) return [];

        var info = new List<ChatMessageData>();

        foreach (var chat in history.MessageList)
            info.Add(new ChatMessageData
            {
                CreateTime = (ulong)chat.SendTime,
                Content = chat.Message,
                ExtraId = (uint)chat.ExtraId,
                SenderId = (uint)chat.SendUid,
                MessageType = chat.ExtraId > 0 ? MsgType.Emoji : MsgType.CustomText
            });

        info.Reverse();

        return info;
    }

    public List<PlayerData> GetFriendPlayerData(List<int>? uids = null)
    {
        var list = new List<PlayerData>();
        uids ??= [.. FriendData.FriendDetailList.Keys];

        foreach (var friend in uids)
        {
            var player = PlayerData.GetPlayerByUid(friend);
            if (player != null) list.Add(player);
        }

        var serverProfile = ConfigManager.Config.ServerOption.ServerProfile;
        list.Add(new PlayerData
        {
            Uid = serverProfile.Uid,
            HeadIcon = serverProfile.HeadIcon,
            Signature = serverProfile.Signature,
            Level = serverProfile.Level,
            WorldLevel = 0,
            Name = serverProfile.Name,
            ChatBubble = serverProfile.ChatBubbleId,
            PersonalCard = serverProfile.PersonalCardId
        });

        return list;
    }

    public List<PlayerData> GetBlackList()
    {
        List<PlayerData> list = [];

        foreach (var friend in FriendData.BlackList)
        {
            var player = PlayerData.GetPlayerByUid(friend);

            if (player != null) list.Add(player);
        }

        return list;
    }

    public List<PlayerData> GetSendApplyList()
    {
        List<PlayerData> list = [];

        foreach (var friend in FriendData.SendApplyList)
        {
            var player = PlayerData.GetPlayerByUid(friend);

            if (player != null) list.Add(player);
        }

        return list;
    }

    public List<PlayerData> GetReceiveApplyList()
    {
        List<PlayerData> list = [];

        foreach (var friend in FriendData.ReceiveApplyList)
        {
            var player = PlayerData.GetPlayerByUid(friend);

            if (player != null) list.Add(player);
        }

        return list;
    }

    public List<PlayerData> GetRandomFriend()
    {
        var list = new List<PlayerData>();

        foreach (var kcp in DanhengListener.Connections.Values)
        {
            if (kcp.State != SessionStateEnum.ACTIVE) continue;
            if (kcp is not Connection connection) continue;
            if (connection.Player?.Uid == Player.Uid) continue;
            var data = connection.Player?.Data;
            if (data == null) continue;
            list.Add(data);
        }

        return list.Take(20).ToList();
    }

    public void RemarkFriendName(int uid, string remarkName)
    {
        if (!FriendData.FriendDetailList.TryGetValue(uid, out var friend)) return;
        friend.RemarkName = remarkName;
    }

    public void MarkFriend(int uid, bool isMark)
    {
        if (!FriendData.FriendDetailList.TryGetValue(uid, out var friend)) return;
        friend.IsMark = isMark;
    }
   public GetFriendRecommendLineupScRsp GetGlobalRecommendLineup(uint challengeId, uint requestType) 
{
   var Log = Logger.GetByClassName();
    
    // 关键错误日志建议始终保留
    if (EnableRecommendLog)
        Log.Info($"[战报请求] >>> 开始处理 | 关卡: {challengeId} | 类型: {requestType} | 当前UID: {Player.Uid}");

    var rsp = new GetFriendRecommendLineupScRsp { Key = challengeId, Retcode = 0, Type = (DLLLEANDAIH)requestType, ONOCJEEBFCI = false };

    // 1. 获取配置
    if (!GameData.ChallengeConfigData.TryGetValue((int)challengeId, out var config)) {
        Log.Error($"[战报错误] 找不到关卡配置! ID: {challengeId}");
        return rsp;
    }
    
    if (EnableRecommendLog)
        Log.Info($"[战报调试] 配置匹配成功: ID {config.ID} | GroupID: {config.GroupID}");

    // --- 核心修改：数据合并与去重逻辑开始 ---

    // 1. 从数据库检索所有记录 (离线玩家数据)
    var dbRecords = DatabaseHelper.sqlSugarScope?.Queryable<FriendRecordData>().ToList() ?? new List<FriendRecordData>();

    // 2. 从内存映射表获取记录 (在线玩家/热数据)
    var memRecords = DatabaseHelper.GetAllInstanceFromMap<FriendRecordData>() ?? new List<FriendRecordData>();

    // 3. 使用 Dictionary 进行去重 (以 Uid 为 Key)
    // 逻辑：先存数据库的，再存内存的。如果 Uid 重复，内存中的最新对象会覆盖数据库的对象。
    var mergedMap = new Dictionary<int, FriendRecordData>();

    foreach (var record in dbRecords) {
        mergedMap[record.Uid] = record;
    }

    foreach (var record in memRecords) {
        mergedMap[record.Uid] = record;
    }

    // 提取合并后的最终列表
    var allRecords = mergedMap.Values.ToList();

    // --- 数据合并与去重逻辑结束 ---

    if (EnableRecommendLog)
        Log.Info($"[战报数据] 合并完成：数据库({dbRecords.Count}) + 内存({memRecords.Count}) -> 去重后共 {allRecords.Count} 条记录");

    foreach (var record in allRecords) {
        var pData = PlayerData.GetPlayerByUid(record.Uid);
        if (pData == null) continue;

        bool isSelf = (record.Uid == Player.Uid);
        // 尝试获取对应 GroupID 的统计数据
        if (record.ChallengeGroupStatistics == null || !record.ChallengeGroupStatistics.TryGetValue((uint)config.GroupID, out var groupStat)) continue;

        var entry = new KEHMGKIHEFN();
        entry.PlayerInfo = pData.ToSimpleProto(isSelf ? FriendOnlineStatus.Online : FriendOnlineStatus.Offline);

        if (EnableRecommendLog)
            Log.Info($"[战报处理] 正在填充条目: {pData.Name} (UID: {record.Uid})");

        if (isSelf) rsp.ONOCJEEBFCI = true;

        // --- 分支判定逻辑 (增加 ?? [] 修复 CS8602 警告) ---

        // A. 虚构叙事 (Story)
        if (config.IsStory() && groupStat.StoryGroupStatistics != null && groupStat.StoryGroupStatistics.TryGetValue(challengeId, out var storyStats)) {
            if (EnableRecommendLog) Log.Info($"[战报识别] >>> [虚构叙事] | 分数: {storyStats.Score}");
            
            entry.ADDCJEJPFEF = new KAMCIOPBPGA { PeakTargetList = { 1, 2, 3 } };
            // 修复警告：storyStats.Lineups 可能为空
            foreach (var dbTeam in storyStats.Lineups ?? [])
                foreach (var av in dbTeam ?? []) 
                    entry.ADDCJEJPFEF.AvatarList.Add(new OILPIACENNH { Id = av.Id, Level = av.Level, Index = av.Index, AvatarType = AvatarType.AvatarFormalType, GGDIIBCDOBB = av.Rank });
        }
        // B. 末日幻影 (Boss)
        else if (config.IsBoss() && groupStat.BossGroupStatistics != null && groupStat.BossGroupStatistics.TryGetValue(challengeId, out var bossStats)) {
            if (EnableRecommendLog) Log.Info($"[战报识别] >>> [末日幻影] | 分数: {bossStats.Score}");
            
            entry.JILKKAJBLJK = new IIGJFPMIGKF { PeakTargetList = { 1, 2, 3 }, BuffId = (uint)config.MazeBuffID, IsUltraBossWin = true, IsHard = true };
            // 修复警告：bossStats.Lineups 可能为空
            foreach (var dbTeam in bossStats.Lineups ?? [])
                foreach (var av in dbTeam ?? []) 
                    entry.JILKKAJBLJK.AvatarList.Add(new OILPIACENNH { Id = av.Id, Level = av.Level, Index = av.Index, AvatarType = AvatarType.AvatarFormalType, GGDIIBCDOBB = av.Rank });
        }
        // C. 忘却之庭 (Memory)
        else if (groupStat.MemoryGroupStatistics != null && groupStat.MemoryGroupStatistics.TryGetValue(challengeId, out var memoryStats)) {
            if (EnableRecommendLog) Log.Info($"[战报识别] >>> [忘却之庭] | 轮数: {memoryStats.RoundCount}");
            
            entry.GIEIDJEEPAC = new FCNOLLFGPCK { PlayerInfo = entry.PlayerInfo, CurLevelStars = memoryStats.Stars, ScoreId = memoryStats.RoundCount, BuffOne = (uint)config.MazeBuffID, BuffTwo = (uint)config.MazeBuffID };
            // 修复警告：memoryStats.Lineups 可能为空
            foreach (var dbTeam in memoryStats.Lineups ?? []) {
                var teamProto = new ChallengeLineupList();
                foreach (var av in dbTeam ?? []) 
                    teamProto.AvatarList.Add(new ChallengeAvatarInfo { Id = av.Id, Level = av.Level, Index = av.Index, AvatarType = AvatarType.AvatarFormalType, GGDIIBCDOBB = av.Rank });
                entry.GIEIDJEEPAC.LineupList.Add(teamProto);
            }
        }
        else {
            if (EnableRecommendLog) Log.Warn($"[战报跳过] UID {record.Uid} 模式未匹配");
            continue;
        }

        rsp.ChallengeRecommendList.Add(entry);
    }

    if (EnableRecommendLog)
        Log.Info($"[战报完成] <<< 处理完毕 | 发送总数: {rsp.ChallengeRecommendList.Count}");
        
    return rsp;
}




    public GetFriendListInfoScRsp ToProto()
    {
        var proto = new GetFriendListInfoScRsp();

        foreach (var player in GetFriendPlayerData())
        {
            var status = Listener.GetActiveConnection(player.Uid) == null
                ? FriendOnlineStatus.Offline
                : FriendOnlineStatus.Online;
            var friend = GetFriendDetailData(player.Uid) ?? new FriendDetailData();

            proto.FriendList.Add(new FriendSimpleInfo
            {
                PlayerInfo = player.ToSimpleProto(status),
                IsMarked = friend.IsMark,
                RemarkName = friend.RemarkName
            });
        }

        foreach (var player in GetBlackList())
        {
            var status = Listener.GetActiveConnection(player.Uid) == null
                ? FriendOnlineStatus.Offline
                : FriendOnlineStatus.Online;
            proto.BlackList.Add(player.ToSimpleProto(status));
        }

        return proto;
    }

    public GetFriendApplyListInfoScRsp ToApplyListProto()
    {
        GetFriendApplyListInfoScRsp proto = new();

        foreach (var player in GetSendApplyList()) proto.SendApplyList.Add((uint)player.Uid);

        foreach (var player in GetReceiveApplyList())
        {
            var status = Listener.GetActiveConnection(player.Uid) == null
                ? FriendOnlineStatus.Offline
                : FriendOnlineStatus.Online;
            proto.ReceiveApplyList.Add(new FriendApplyInfo
            {
                PlayerInfo = player.ToSimpleProto(status)
            });
        }

        return proto;
    }
}