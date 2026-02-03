using System.ComponentModel.DataAnnotations.Schema;
using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Database.Quests;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.Database.Friend; // 添加这一行
using SqlSugar;
using LineupInfo = EggLink.DanhengServer.Database.Lineup.LineupInfo;

namespace EggLink.DanhengServer.Database.Player;

[SugarTable("Player")]
public class PlayerData : BaseDatabaseDataHelper
{
    public string? Name { get; set; } = "";
    public string? Signature { get; set; } = "";
    public int Birthday { get; set; } = 0;
    public int CurBasicType { get; set; } = 8001;
    public int HeadIcon { get; set; } = 208001;
    public int PhoneTheme { get; set; } = 221000;
    public int ChatBubble { get; set; } = 220000;
    public int PersonalCard { get; set; } = 253000;
    public int PhoneCase { get; set; } = 254000;
    public int CurrentBgm { get; set; } = 210007;
    public int CurrentPamSkin { get; set; } = 252000;
    public bool IsGenderSet { get; set; } = false;
    public Gender CurrentGender { get; set; } = Gender.Man;
    public int Level { get; set; } = 1;
    public int Exp { get; set; } = 0;
    public int WorldLevel { get; set; } = 0;
    public int Scoin { get; set; } = 0; // Credits
    public int Hcoin { get; set; } = 0; // Jade
    public int Mcoin { get; set; } = 0; // Crystals
    public int TalentPoints { get; set; } = 0; // Rogue talent points
	// 在 PlayerData 类里面添加这个属性
	// 0 或者 10 代表初始状态（只解锁世界1）
	public int RogueUnlockProgress { get; set; } = 110;
	// =========================================================================
    // 【新增】模拟宇宙已通关区域 ID 记录
    // 用途：存储如 "110,120,130,131" 的字符串，用于判断首通奖励和关卡解锁
    // =========================================================================
    public string RogueFinishedAreaIds { get; set; } = "";
    // 在 PlayerData 类中添加
    public uint RogueScore { get; set; } = 0; // 当前周累积的模拟宇宙积分
    public long LastRogueScoreUpdate { get; set; } = 0; // 上次积分更新的时间戳（秒）
	// 在 EggLink.DanhengServer.Database.Player.PlayerData 类中添加
	// --- 新增沉浸器字段 ---
    /// <summary>
    /// 沉浸器数量 (ID 33)
    /// </summary>
    public int ImmersiveArtifact { get; set; } = 0;
	/// <summary>
    /// 已解锁的命途 ID 列表 (如 "1,2,7")
    /// 默认值可以设为 "1"，代表初始解锁存护
    /// </summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string UnlockedAeonIds { get; set; } = "1";
    /// <summary>
    /// 已解锁的合成配方 ID 列表
    /// 使用 SqlSugar 的 IsJson 标签将其在数据库中存为字符串
    /// </summary>
    [SugarColumn(IsJson = true)] 
    public List<int> UnlockedRecipes { get; set; } = new();
	
	public string TakenRogueRewardIds { get; set; } = "";
	[NotMapped] // 告诉 ORM 忽略这个字段
    public List<uint> TakenRogueRewardList { get; set; } = new();
    public int Pet { get; set; } = 0;
    [SugarColumn(IsNullable = true)] public int CurMusicLevel { get; set; }

    public int Stamina { get; set; } = 300;
    public double StaminaReserve { get; set; } = 0;
    public long NextStaminaRecover { get; set; } = 0;

    [SugarColumn(IsNullable = true, IsJson = true)]
    public Position? Pos { get; set; }

    [SugarColumn(IsNullable = true, IsJson = true)]
    public Position? Rot { get; set; }

    [SugarColumn(IsJson = true)] public PlayerHeadFrameInfo HeadFrame { get; set; } = new();

    [SugarColumn(IsNullable = true)] public int PlaneId { get; set; }

    [SugarColumn(IsNullable = true)] public int FloorId { get; set; }

    [SugarColumn(IsNullable = true)] public int EntryId { get; set; }

    [SugarColumn(IsNullable = true)] public long LastActiveTime { get; set; }

    [SugarColumn(IsJson = true)] public List<int> TakenLevelReward { get; set; } = [];
    [SugarColumn(IsJson = true)] public PrivacySettingsPb PrivacySettings { get; set; } = new();

    public static PlayerData? GetPlayerByUid(long uid)
    {
        var result = DatabaseHelper.Instance?.GetInstance<PlayerData>((int)uid);
        return result;
    }

    public PlayerBasicInfo ToProto()
    {
        return new PlayerBasicInfo
        {
            Nickname = Name,
            Level = (uint)Level,
            Exp = (uint)Exp,
            WorldLevel = (uint)WorldLevel,
            Scoin = (uint)Scoin,
            Hcoin = (uint)Hcoin,
            Mcoin = (uint)Mcoin,
            Stamina = (uint)Stamina
        };
    }

    public LobbyPlayerBasicInfo ToLobbyProto()
    {
        return new LobbyPlayerBasicInfo
        {
            Nickname = Name,
            Level = (uint)Level,
            LobbyHeadIconId = (uint)HeadIcon,
            Platform = PlatformType.Pc,
            Uid = (uint)Uid
        };
    }

    public PlayerSimpleInfo ToSimpleProto(FriendOnlineStatus status)
    {
        if (!GameData.ChatBubbleConfigData.ContainsKey(ChatBubble)) // to avoid npe
            ChatBubble = 220000;

        var info = new PlayerSimpleInfo
        {
            Nickname = Name,
            Level = (uint)Level,
            Signature = Signature,
            Uid = (uint)Uid,
            OnlineStatus = status,
            HeadIcon = (uint)HeadIcon,
            Platform = PlatformType.Pc,
            LogoutTime = LastActiveTime,
            ChatBubble = (uint)ChatBubble,
            PersonalCard = (uint)PersonalCard,
            HeadIconFrameInfo = HeadFrame.ToProto()
        };

        var pos = 0;
        var instance = DatabaseHelper.Instance!.GetInstance<AvatarData>(Uid);
        if (instance == null)
        {
            // Handle server profile
            var serverProfile = ConfigManager.Config.ServerOption.ServerProfile;
            if (Uid == serverProfile.Uid)
            {
                info.OnlineStatus = FriendOnlineStatus.Online;
                info.AssistInfoList.AddRange(
                    serverProfile.AssistInfo.Select((x, index) =>
                        new AssistSimpleInfo
                        {
                            AvatarId = (uint)x.AvatarId,
                            Level = (uint)x.Level,
                            DressedSkinId = (uint)x.SkinId,
                            Pos = (uint)index
                        }));
            }

            return info;
        }

        foreach (var avatar in instance.AssistAvatars.Select(
                     assist => instance.FormalAvatars.Find(x => x.AvatarId == assist)))
            if (avatar != null)
                info.AssistInfoList.Add(new AssistSimpleInfo
                {
                    AvatarId = (uint)avatar.AvatarId,
                    Level = (uint)avatar.Level,
                    DressedSkinId = (uint)avatar.GetCurPathInfo().Skin,
                    Pos = (uint)pos++
                });

        return info;
    }

    public PlayerDetailInfo ToDetailProto()
    {
        var info = new PlayerDetailInfo
        {
            Nickname = Name,
            Level = (uint)Level,
            Signature = Signature,
            IsBanned = false,
            HeadIcon = (uint)HeadIcon,
            PersonalCard = (uint)PersonalCard,
            Platform = PlatformType.Pc,
            Uid = (uint)Uid,
            WorldLevel = (uint)WorldLevel,
            EMOBIJBDKEI = true, // ShowDisplayAvatar
            RecordInfo = new PlayerRecordInfo(),
            PrivacySettings = PrivacySettings.ToProto(),
            HeadFrame = HeadFrame.ToProto()
        };

        var avatarInfo = DatabaseHelper.Instance!.GetInstance<AvatarData>(Uid);
        var inventoryInfo = DatabaseHelper.Instance.GetInstance<InventoryData>(Uid);
        var questInfo = DatabaseHelper.Instance.GetInstance<QuestData>(Uid);
		// 【新增】获取数据库中的详细战绩数据
    	var recordData = DatabaseHelper.Instance.GetInstance<FriendRecordData>(Uid);

        if (avatarInfo == null || inventoryInfo == null || questInfo == null)
        {
            // Handle server profile
            var serverProfile = ConfigManager.Config.ServerOption.ServerProfile;
            if (Uid == serverProfile.Uid)
                info.AssistAvatarList.AddRange(
                    serverProfile.AssistInfo.Select((x, index) =>
                        new DisplayAvatarDetailInfo
                        {
                            AvatarId = (uint)x.AvatarId,
                            Level = (uint)x.Level,
                            DressedSkinId = (uint)x.SkinId,
                            Pos = (uint)index
                        }));
            return info;
        }

        info.RecordInfo = new PlayerRecordInfo
        {
            CollectAvatarCount = (uint)avatarInfo.FormalAvatars.Count,
            CollectEquipmentCount = (uint)inventoryInfo.EquipmentItems.Select(x => x.ItemId).ToHashSet().Count,
            CollectRelicCount = (uint)inventoryInfo.RelicItems.Count,
            CollectAchievementCount = (uint)GameData.AchievementDataData.Values.Select(x => x.QuestID).ToHashSet()
                .Count(x => questInfo.Quests.GetValueOrDefault(x)?.QuestStatus is QuestStatus.QuestFinish
                    or QuestStatus.QuestClose), // count finished achievements
            CollectionInfo = new PlayerCollectionInfo(),
            CollectDiscCount = (uint)GameData.BackGroundMusicData.Count
        };
    	// 1. 初始化显示设置
		info.ONKHLHOJHGN = new PlayerDisplaySettings();

		if (recordData != null)
		{
    	foreach (var groupStat in recordData.ChallengeGroupStatistics.Values)
    {	
        var entry = new IHKGNJDNALJ
        {
            GroupId = groupStat.GroupId,           
            GroupTotalStars = groupStat.GroupTotalStars,
            // 模式总层数：通常忘却12，虚构/末日4 (控制进度条长度)
            FCCDILGGOCI = groupStat.GroupId >= 2000 ? 4u : 12u 
        };

        // --- 核心修正：区分进度显示 (层数 vs 分数) ---
        if (groupStat.GroupId >= 2001) 
        {
            // B. 虚构叙事 & C. 末日幻影：首页显示总积分
            // 逻辑：从该组所有层级中找出最高分
            uint maxScore = 0;
            if (groupStat.StoryGroupStatistics != null)
                maxScore = groupStat.StoryGroupStatistics.Values.Select(x => x.Score).DefaultIfEmpty(0u).Max();
            else if (groupStat.BossGroupStatistics != null)
                maxScore = groupStat.BossGroupStatistics.Values.Select(x => x.Score).DefaultIfEmpty(0u).Max();
            
            entry.JGMIPMDPPIJ = maxScore; // 首页图标下将显示积分数字
        }
        else 
        {
            // A. 忘却之庭：首页显示最高层数
            uint maxFloor = 0;
            if (groupStat.MemoryGroupStatistics != null)
                maxFloor = groupStat.MemoryGroupStatistics.Values.Select(x => x.Level).DefaultIfEmpty(0u).Max();
            
            entry.JGMIPMDPPIJ = maxFloor; // 首页图标下将显示层数数字
        }

        info.ONKHLHOJHGN.ChallengeList.Add(entry);
    	}
		}
        var pos = 0;
        foreach (var avatar in avatarInfo.AssistAvatars.Select(assist =>
                     avatarInfo.FormalAvatars.Find(x => x.BaseAvatarId == assist)))
            if (avatar != null)
                info.AssistAvatarList.Add(avatar.ToDetailProto(pos++,
                    new PlayerDataCollection(this, inventoryInfo, new LineupInfo())));

        pos = 0;
        foreach (var avatar in avatarInfo.DisplayAvatars.Select(display =>
                     avatarInfo.FormalAvatars.Find(x => x.BaseAvatarId == display)))
            if (avatar != null)
                info.DisplayAvatarList.Add(avatar.ToDetailProto(pos++,
                    new PlayerDataCollection(this, inventoryInfo, new LineupInfo())));

        return info;
    }
}

public class PlayerHeadFrameInfo
{
    public long HeadFrameExpireTime { get; set; }
    public uint HeadFrameId { get; set; }

    public HeadFrameInfo ToProto()
    {
        return new HeadFrameInfo
        {
            HeadFrameExpireTime = HeadFrameExpireTime,
            HeadFrameId = HeadFrameId
        };
    }
}

public class PrivacySettingsPb
{
    public bool DisplayChallengeLineup { get; set; } = true;
    public bool DisplayActiveState { get; set; } = true;
    public bool DisplayRecentlyState { get; set; } = true;
    public bool DisplayBattleRecord { get; set; } = true;
    public bool DisplayCollection { get; set; } = true;

    public PrivacySettings ToProto()
    {
        return new PrivacySettings
        {
            DisplayChallengeLineup = DisplayChallengeLineup,
            DisplayActiveState = DisplayActiveState,
            DisplayRecentlyState = DisplayRecentlyState,
            DisplayBattleRecord = DisplayBattleRecord,
            DisplayCollection = DisplayCollection
        };
    }

    public PlayerSettingInfo ToSettingProto()
    {
        return new PlayerSettingInfo
        {
            DisplayChallengeLineup = DisplayChallengeLineup,
            DisplayActiveState = DisplayActiveState,
            DisplayRecentlyState = DisplayRecentlyState,
            DisplayBattleRecord = DisplayBattleRecord,
            DisplayCollection = DisplayCollection,
            ExtraSettingsInfo = new PlayerExtraSettingsInfo()
        };
    }
}
