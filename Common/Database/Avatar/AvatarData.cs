using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Database.Player;
using EggLink.DanhengServer.Enums.Avatar;
using EggLink.DanhengServer.Enums.Item;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using SqlSugar;
using LineupInfo = EggLink.DanhengServer.Database.Lineup.LineupInfo;

namespace EggLink.DanhengServer.Database.Avatar;

[SugarTable("Avatar")]
public class AvatarData : BaseDatabaseDataHelper
{
    [SugarColumn(IsJson = true)] public List<OldAvatarInfo> Avatars { get; set; } = [];
    [SugarColumn(IsJson = true)] public List<FormalAvatarInfo> FormalAvatars { get; set; } = [];
    [SugarColumn(IsJson = true)] public List<SpecialAvatarInfo> TrialAvatars { get; set; } = [];

    [SugarColumn(IsJson = true)] public List<int> AssistAvatars { get; set; } = [];

    [SugarColumn(IsJson = true)] public List<int> DisplayAvatars { get; set; } = [];

    public string DatabaseVersion { get; set; } = "0";
}

public abstract class BaseAvatarInfo
{
    public int BaseAvatarId { get; set; }
    public int AvatarId { get; set; } // special avatar id / base avatar id
    public int Promotion { get; set; }
    public int Level { get; set; }
    public int CurrentHp { get; set; } = 10000;
    public int CurrentSp { get; set; }
    public int ExtraLineupHp { get; set; } = 10000;
    public int ExtraLineupSp { get; set; }
    public Dictionary<int, PathInfo> PathInfos { get; set; } = [];

    public void SetCurHp(int value, bool isExtraLineup)
    {
        if (isExtraLineup)
            ExtraLineupHp = value;
        else
            CurrentHp = value;
    }

    public void SetCurSp(int value, bool isExtraLineup)
    {
        if (isExtraLineup)
            ExtraLineupSp = value;
        else
            CurrentSp = value;
    }

    public int GetCurHp(bool isExtraLineup)
    {
        return isExtraLineup ? ExtraLineupHp : CurrentHp;
    }

    public int GetCurSp(bool isExtraLineup)
    {
        return isExtraLineup ? ExtraLineupSp : CurrentSp;
    }

    public PathInfo GetCurPathInfo()
    {
        if (PathInfos.TryGetValue(AvatarId, out var info)) return info;

        PathInfos.Add(AvatarId, new PathInfo(AvatarId));
        return PathInfos[AvatarId];
    }

    public PathInfo? GetPathInfo(int pathId)
    {
        if (PathInfos.TryGetValue(pathId, out var value)) return value;
        return null;
    }

    public abstract BattleAvatar ToBattleProto(PlayerDataCollection collection,
        AvatarType avatarType = AvatarType.AvatarFormalType);

    public abstract LineupAvatar ToLineupInfo(int slot, LineupInfo info,
        AvatarType avatarType = AvatarType.AvatarFormalType);

    public abstract Proto.Avatar ToProto();
}

public class FormalAvatarInfo : BaseAvatarInfo
{
    public FormalAvatarInfo()
    {
        // only for db
    }

    public FormalAvatarInfo(int baseAvatarId, int avatarId, bool addSkills)
    {
        // TODO add skills
        BaseAvatarId = baseAvatarId;
        AvatarId = avatarId;
        if (addSkills) CheckPathSkillTree();
    }

    public int Exp { get; set; }
    public int Rewards { get; set; }
    public long Timestamp { get; set; }
    public bool IsMarked { get; set; } = false;

    public bool HasTakenReward(int promotion)
{
    // 强制检查：必须确保 promotion 参数是 1, 3, 5 之一
    // 否则直接判定为已领，防止通过包体注入非 1/3/5 的数值来刷票
    if (promotion != 1 && promotion != 3 && promotion != 5) return true;

    return (Rewards & (1 << promotion)) != 0;
}

    public void ValidateHero(Gender gender)
    {
        foreach (var pathInfo in PathInfos.ToArray())
        {
            if (!GameData.MultiplePathAvatarConfigData.TryGetValue(pathInfo.Key, out var path)) continue;
            if (path.Gender == GenderTypeEnum.GENDER_NONE) continue;
            if (path.Gender == (GenderTypeEnum)gender) continue;
            PathInfos.Remove(pathInfo.Key);
        }
    }

    public void CheckPathSkillTree()
    {
        if (!GameData.AvatarConfigData.TryGetValue(AvatarId, out var excel)) return;
        if (PathInfos.ContainsKey(AvatarId)) return;
        if (excel.DefaultSkillTree[0].Count == 0) return;

        // create path info
        var path = new PathInfo(AvatarId);
        path.GetSkillTree();

        PathInfos.Add(AvatarId, path);
    }

   public bool TakeReward(int promotion)
	{
    // 核心拦截：如果 HasTakenReward 返回 true，说明位掩码中已存在该阶位记录
    if (HasTakenReward(promotion)) 
    {
        return false; // 告知处理器：此奖励已领过
    }

    // 执行位运算标记
    Rewards |= 1 << promotion;
    return true; // 告知处理器：标记成功，可以发放奖励
}

    public override Proto.Avatar ToProto()
{
    var proto = new Proto.Avatar
    {
        BaseAvatarId = (uint)BaseAvatarId,
        Level = (uint)Level,
        Exp = (uint)Exp,
        Promotion = (uint)Promotion,
        Rank = (uint)GetCurPathInfo().Rank,
        FirstMetTimeStamp = (ulong)Timestamp,
        IsMarked = IsMarked,
        DressedSkinId = (uint)GetCurPathInfo().Skin,
        CurEnhanceId = (uint)GetCurPathInfo().EnhanceId
    };

    foreach (var item in GetCurPathInfo().Relic)
        proto.EquipRelicList.Add(new EquipRelic
        {
            RelicUniqueId = (uint)item.Value,
            Type = (uint)item.Key
        });

    if (GetCurPathInfo().EquipId != 0) proto.EquipmentUniqueId = (uint)GetCurPathInfo().EquipId;

    foreach (var skill in GetCurPathInfo().GetSkillTree())
        proto.SkilltreeList.Add(new AvatarSkillTree
        {
            PointId = (uint)skill.Key,
            Level = (uint)skill.Value
        });

    // --- 核心修复点：同步已领取状态给客户端 UI ---
    // 官方晋阶奖励固定在 1, 3, 5 阶。
    // 你之前的循环条件是 i < Promotion，如果当前是 2 阶领 2 阶奖励，i 永远点不到 2。
    // 在 AvatarData.cs 的 FormalAvatarInfo.ToProto() 中：
	int[] checkLevels = { 1, 3, 5 };
	foreach (var lv in checkLevels)
	{
    // 逻辑：只要角色等级够领该档位，且 Rewards 位标记已领，就发给客户端
    if (this.Promotion >= lv && this.HasTakenReward(lv)) 
    {
        proto.HasTakenPromotionRewardList.Add((uint)lv);
    }
}

    return proto;
}

    public override LineupAvatar ToLineupInfo(int slot, LineupInfo info,
        AvatarType avatarType = AvatarType.AvatarFormalType)
    {
        return new LineupAvatar
        {
            Id = (uint)BaseAvatarId,
            Slot = (uint)slot,
            AvatarType = avatarType,
            Hp = info.IsExtraLineup() ? (uint)ExtraLineupHp : (uint)CurrentHp,
            SpBar = new SpBarInfo
            {
                CurSp = info.IsExtraLineup() ? (uint)ExtraLineupSp : (uint)CurrentSp,
                MaxSp = 10000
            }
        };
    }

    #region Battle Proto

   public override BattleAvatar ToBattleProto(PlayerDataCollection collection, AvatarType avatarType = AvatarType.AvatarFormalType)
{
    // --- 1. 等级压制预处理 ---
    uint correctedLevel = (uint)Level;
    uint correctedPromotion = (uint)Promotion;

    if (avatarType == AvatarType.AvatarAssistType)
    {
        // 获取当前借人玩家的均衡等级 (0-6)
        int playerWorldLevel = collection.PlayerData.WorldLevel;

        // 根据均衡等级，从配置表里查这个角色在当前位面的最高上限
        if (GameData.AvatarPromotionConfigData.TryGetValue(BaseAvatarId * 10 + playerWorldLevel, out var config))
        {
            if (correctedLevel > (uint)config.MaxLevel)
            {
                correctedLevel = (uint)config.MaxLevel;
                correctedPromotion = (uint)playerWorldLevel;
            }
        }
    }

    // --- 2. 构造基础 Proto ---
    var proto = CreateBaseProto(collection, avatarType);
    
    // 覆盖为压制后的等级
    proto.Level = correctedLevel;
    proto.Promotion = correctedPromotion;

    var isUpgradable = IsUpgradableType(avatarType);
    if (!GameData.AvatarConfigData.TryGetValue(AvatarId, out var avatarConf))
        return proto;

    if (isUpgradable) ApplyMaxLevel(proto);

    // --- 3. 技能处理 (传入 correctedPromotion 进行压制) ---
    foreach (var (skillId, level) in GetCurPathInfo().GetSkillTree())
    {
        var finalLevel = isUpgradable ? GetUpgradedSkillLevel(skillId, level) : level;

        // 助战技能压制：防止 20 级角色放 10 级大招导致数据异常
        if (avatarType == AvatarType.AvatarAssistType)
        {
            uint skillCap = correctedPromotion + 3; // 经验公式：晋阶+3
            if (finalLevel > (int)skillCap) finalLevel = (int)skillCap;
        }

        proto.SkilltreeList.Add(new AvatarSkillTree { PointId = (uint)skillId, Level = (uint)finalLevel });
    }

    // --- 4. 遗器与光锥 (透传 collection.InventoryData) ---
    ProcessRelics(proto, collection, isUpgradable);
    ProcessEquipment(proto, collection, isUpgradable, avatarConf);

    return proto;
}

    private BattleAvatar CreateBaseProto(PlayerDataCollection collection, AvatarType avatarType)
    {
        var isBattle = collection.LineupInfo.LineupType != 0;

        return new BattleAvatar
        {
            Id = (uint)AvatarId,
            AvatarType = avatarType,
            Level = (uint)Level,
            Promotion = (uint)Promotion,
            Rank = (uint)GetCurPathInfo().Rank,
            Index = (uint)collection.LineupInfo.GetSlot(BaseAvatarId),
            Hp = (uint)GetCurHp(isBattle),
            SpBar = new SpBarInfo { CurSp = (uint)GetCurSp(isBattle), MaxSp = 10000 },
            WorldLevel = (uint)collection.PlayerData.WorldLevel,
            AvatarEnhanceId = (uint)GetCurPathInfo().EnhanceId
        };
    }

    private static bool IsUpgradableType(AvatarType avatarType) =>
        avatarType is AvatarType.AvatarGridFightType or AvatarType.AvatarUpgradeAvailableType;

    private static void ApplyMaxLevel(BattleAvatar proto)
    {
        proto.Level = 80;
        proto.Promotion = 6;
    }

    private void ProcessSkills(BattleAvatar proto, bool isUpgradable, uint correctedPromotion, AvatarType avatarType)
    {
        foreach (var (skillId, level) in GetCurPathInfo().GetSkillTree())
        {
            var finalLevel = isUpgradable ? GetUpgradedSkillLevel(skillId, level) : level;

            // 如果是助战，执行技能等级压制 (晋阶 + 3 左右是安全阈值)
            if (avatarType == AvatarType.AvatarAssistType)
            {
                uint skillCap = correctedPromotion + 3;
                if (finalLevel > (int)skillCap) finalLevel = (int)skillCap;
            }

            proto.SkilltreeList.Add(new AvatarSkillTree
            {
                PointId = (uint)skillId,
                Level = (uint)finalLevel
            });
        }
    }

    private static int GetUpgradedSkillLevel(int skillId, int currentLevel)
    {
        var maxLevel = GameData.AvatarSkillTreeConfigData.GetValueOrDefault(skillId * 100 + currentLevel)?.MaxLevel ?? 1;
        return Math.Max(Math.Max(1, maxLevel - 2), currentLevel);
    }

    private void ProcessRelics(BattleAvatar proto, PlayerDataCollection collection, bool isUpgradable)
    {
        var relicUpgradeType = GameData.UpgradeAvatarSubTypeData.GetValueOrDefault((uint)AvatarId)?.SubType
                             ?? UpgradeAvatarSubRelicTypeEnum.Base;
        var relicRecommend = GameData.AvatarRelicRecommendData.GetValueOrDefault((uint)AvatarId);

        // Ensure all relic slots exist
        var equippedRelics = GetCurPathInfo().Relic;
        for (var slot = 1; slot <= 6; slot++)
            equippedRelics.TryAdd(slot, 0);

        foreach (var (slot, relicId) in equippedRelics)
        {
            var relic = CreateRelicForSlot(slot, relicId, collection, isUpgradable, relicUpgradeType, relicRecommend);
            if (relic != null)
                proto.RelicList.Add(relic);
        }
    }

    private BattleRelic? CreateRelicForSlot(int slot, int relicId, PlayerDataCollection collection,
        bool isUpgradable, UpgradeAvatarSubRelicTypeEnum upgradeType, AvatarRelicRecommendExcel? recommend)
    {
        var item = collection.InventoryData.RelicItems.Find(x => x.UniqueId == relicId);

        // Use existing relic if not upgradable or already maxed
        if (item != null && (!isUpgradable || item.Level >= 15 || recommend == null))
            return CreateRelicFromItem(item);

        // Create internal relic for upgrade scenario
        return isUpgradable ? CreateInternalRelic(slot, upgradeType, recommend) : null;
    }

    private static BattleRelic CreateRelicFromItem(ItemData item)
    {
        var relic = new BattleRelic
        {
            Id = (uint)item.ItemId,
            UniqueId = (uint)item.UniqueId,
            Level = (uint)item.Level,
            MainAffixId = (uint)item.MainAffix
        };

        item.SubAffixes.ForEach(sub => relic.SubAffixList.Add(sub.ToProto()));
        return relic;
    }

    private BattleRelic? CreateInternalRelic(int slot, UpgradeAvatarSubRelicTypeEnum upgradeType, AvatarRelicRecommendExcel? recommend)
    {
        if (recommend == null) return null;

        var slotType = (RelicTypeEnum)slot;
        var relicSet = GetRecommendedRelicSet(slot, recommend);
        var relicInfo = GetRelicUpgradeInfo(upgradeType, slotType);
        var relicItem = FindRelicConfig(relicSet, slotType);

        if (relicInfo == null || relicItem == null) return null;

        var mainAffixId = GetMainAffixId(slot, recommend, relicItem);
        if (mainAffixId == 0) return null;

        return BuildBattleRelic(relicItem, mainAffixId, relicInfo);
    }

    private static uint GetRecommendedRelicSet(int slot, AvatarRelicRecommendExcel recommend) =>
        slot <= 4 ? recommend.Set4IDList.First() : recommend.Set2IDList.First();

    private UpgradeAvatarSubRelicExcel? GetRelicUpgradeInfo(UpgradeAvatarSubRelicTypeEnum upgradeType, RelicTypeEnum slotType) =>
        GameData.UpgradeAvatarSubRelicData.GetValueOrDefault(upgradeType, [])
            .GetValueOrDefault(RarityEnum.CombatPowerRelicRarity5, [])
            .GetValueOrDefault(15u, [])
            .GetValueOrDefault(slotType);

    private static RelicConfigExcel? FindRelicConfig(uint relicSet, RelicTypeEnum slotType) =>
        GameData.RelicConfigData.Values.FirstOrDefault(x =>
            x.SetID == relicSet && x.Rarity == RarityEnum.CombatPowerRelicRarity5 && x.Type == slotType);

    private uint GetMainAffixId(int slot, AvatarRelicRecommendExcel recommend, RelicConfigExcel relicItem)
    {
        var mainAffix = recommend.PropertyList.FirstOrDefault(x => x.RelicType == (RelicTypeEnum)slot)?.PropertyType;

        if (mainAffix == null)
            return GetRandomRelicMainAffix(relicItem.ID);

        return (uint)(GameData.RelicMainAffixData[relicItem.MainAffixGroup].Values
            .FirstOrDefault(x => x.Property == mainAffix)?.AffixID ?? 0);
    }

    private static BattleRelic BuildBattleRelic(RelicConfigExcel relicItem, uint mainAffixId, UpgradeAvatarSubRelicExcel relicInfo)
    {
        var battleRelic = new BattleRelic
        {
            Id = (uint)relicItem.ID,
            Level = 15,
            MainAffixId = mainAffixId
        };

        foreach (var relic in relicInfo.SubAffixes)
        {
            var subAffixConf = GameData.RelicSubAffixData[relicItem.SubAffixGroup].Values
                .FirstOrDefault(x => x.Property == relic.AffixProperty);
            if (subAffixConf == null) continue;

            battleRelic.SubAffixList.Add(new RelicAffix
            {
                AffixId = (uint)subAffixConf.AffixID,
                Cnt = relic.AffixCount,
                Step = (uint)(relic.AffixCount * subAffixConf.StepNum)
            });
        }

        return battleRelic;
    }

    private void ProcessEquipment(BattleAvatar proto, PlayerDataCollection collection, bool isUpgradable, AvatarConfigExcel avatarConf)
    {
        var equipId = GetCurPathInfo().EquipId;
        var equipData = GetCurPathInfo().EquipData;

        if (equipId != 0)
        {
            var item = collection.InventoryData.EquipmentItems.Find(x => x.UniqueId == equipId);
            if (item != null)
                proto.EquipmentList.Add(CreateEquipmentFromItem(item, isUpgradable, avatarConf));
        }
        else if (equipData != null)
        {
            proto.EquipmentList.Add(CreateEquipmentFromData(equipData));
        }
        else if (isUpgradable)
        {
            var internalEquip = CreateInternalEquipment(avatarConf);
            if (internalEquip != null)
                proto.EquipmentList.Add(internalEquip);
        }
    }

    private BattleEquipment CreateEquipmentFromItem(ItemData item, bool isUpgradable, AvatarConfigExcel avatarConf)
    {
        var (itemId, level, promotion, rank) = (item.ItemId, item.Level, item.Promotion, item.Rank);

        if (isUpgradable)
            (itemId, level, promotion, rank) = UpgradeEquipment(itemId, rank, avatarConf);

        return new BattleEquipment
        {
            Id = (uint)itemId,
            Level = (uint)level,
            Promotion = (uint)promotion,
            Rank = (uint)rank
        };
    }

    private (int itemId, int level, int promotion, int rank) UpgradeEquipment(int itemId, int rank, AvatarConfigExcel avatarConf)
    {
        if (GameData.EquipmentConfigData.TryGetValue(itemId, out var equipConf) &&
            equipConf.Rarity is RarityEnum.CombatPowerLightconeRarity3)
        {
            if (GameData.UpgradeAvatarEquipmentData.TryGetValue(avatarConf.AvatarBaseType, out var equipInfo))
            {
                itemId = (int)equipInfo.EquipmentId;
                equipConf = GameData.EquipmentConfigData.GetValueOrDefault(itemId);
            }

            return (itemId, 80, equipConf?.MaxPromotion ?? 6, 1);
        }

        return (itemId, 80, 6, rank);
    }

    private static BattleEquipment CreateEquipmentFromData(ItemData data) => new()
    {
        Id = (uint)data.ItemId,
        Level = (uint)data.Level,
        Promotion = (uint)data.Promotion,
        Rank = (uint)data.Rank
    };

    private static BattleEquipment? CreateInternalEquipment(AvatarConfigExcel avatarConf)
    {
        if (!GameData.UpgradeAvatarEquipmentData.TryGetValue(avatarConf.AvatarBaseType, out var equipInfo))
            return null;

        return new BattleEquipment
        {
            Id = equipInfo.EquipmentId,
            Level = 80,
            Promotion = 6,
            Rank = 1
        };
    }

    public uint GetRandomRelicMainAffix(int itemId)
    {
        GameData.RelicConfigData.TryGetValue(itemId, out var config);
        if (config == null) return 0;
        GameData.RelicMainAffixData.TryGetValue(config.MainAffixGroup, out var affixes);
        if (affixes == null) return 0;
        List<uint> affixList = [];
        affixList.AddRange(from affix in affixes.Values select (uint)affix.AffixID);
        return affixList.RandomElement();
    }

    #endregion

    public ChallengePeakAvatar ToPeakAvatarProto()
    {
        return new ChallengePeakAvatar
        {
            AvatarId = (uint)AvatarId,
            EquipmentUniqueId = (uint)GetCurPathInfo().EquipId,
            RelicList =
            {
                GetCurPathInfo().Relic.Select(relic => new EquipRelic
                {
                    Type = (uint)relic.Key,
                    RelicUniqueId = (uint)relic.Value
                })
            }
        };
    }

    public List<MultiPathAvatarInfo> ToAvatarPathProto()
    {
        var res = new List<MultiPathAvatarInfo>();

        foreach (var pathInfo in PathInfos.Values)
        {
            var proto = new MultiPathAvatarInfo
            {
                AvatarId = (MultiPathAvatarType)pathInfo.PathId,
                Rank = (uint)pathInfo.Rank,
                PathEquipmentId = (uint)pathInfo.EquipId,
                DressedSkinId = (uint)pathInfo.Skin,
                CurEnhanceId = (uint)GetCurPathInfo().EnhanceId
            };

            foreach (var skill in pathInfo.GetSkillTree())
                proto.MultiPathSkillTree.Add(new AvatarSkillTree
                {
                    PointId = (uint)skill.Key,
                    Level = (uint)skill.Value
                });

            foreach (var relic in pathInfo.Relic)
                proto.EquipRelicList.Add(new EquipRelic
                {
                    Type = (uint)relic.Key,
                    RelicUniqueId = (uint)relic.Value
                });

            res.Add(proto);
        }

        return res;
    }

  public DisplayAvatarDetailInfo ToDetailProto(int pos, PlayerDataCollection collection)
    {
        var proto = new DisplayAvatarDetailInfo
        {
            AvatarId = (uint)AvatarId,
            Level = (uint)Level,
            Exp = (uint)Exp,
            Promotion = (uint)Promotion,
            Rank = (uint)GetCurPathInfo().Rank,
            Pos = (uint)pos,
            DressedSkinId = (uint)GetCurPathInfo().Skin
        };

        var inventory = collection.InventoryData;
        foreach (var item in GetCurPathInfo().Relic)
        {
            var relic = inventory.RelicItems.Find(x => x.UniqueId == item.Value);
            // 确保这里的 if 有一对完整的括号
            if (relic != null)
            {
                proto.RelicList.Add(relic.ToDisplayRelicProto());
            }
        } // foreach 结束

        if (GetCurPathInfo().EquipId != 0)
        {
            var equip = inventory.EquipmentItems.Find(x => x.UniqueId == GetCurPathInfo().EquipId);
            // 确保这里的 if 有一对完整的括号
            if (equip != null)
            {
                proto.Equipment = equip.ToDisplayEquipmentProto();
            }
        } // if (EquipId != 0) 结束

        foreach (var skill in GetCurPathInfo().GetSkillTree())
        {
            proto.SkilltreeList.Add(new AvatarSkillTree
            {
                PointId = (uint)skill.Key,
                Level = (uint)skill.Value
            });
        } // foreach 结束

        return proto;
    } // 方法结束标志，检查这里是否漏了！
} // 类 FormalAvatarInfo 结束标志，检查这里是否漏了！

public class SpecialAvatarInfo : BaseAvatarInfo
{
    public int SpecialAvatarId { get; set; }


    public void CheckLevel(int worldLevel)
    {
        if (!GameData.SpecialAvatarData.TryGetValue(AvatarId * 10 + worldLevel, out var specialAvatar))
            if (!GameData.SpecialAvatarData.TryGetValue(AvatarId * 10 + 1, out specialAvatar))
                return;

        Level = specialAvatar.Level;
        Promotion = specialAvatar.Promotion;
        GetCurPathInfo().Rank = specialAvatar.Rank;
        GetCurPathInfo().EquipData = new ItemData
        {
            ItemId = specialAvatar.EquipmentID,
            Level = specialAvatar.EquipmentLevel,
            Promotion = specialAvatar.EquipmentPromotion,
            Rank = specialAvatar.EquipmentRank
        };
    }

    public override Proto.Avatar ToProto()
    {
        var proto = new Proto.Avatar
        {
            BaseAvatarId = (uint)BaseAvatarId,
            Level = (uint)Level,
            Promotion = (uint)Promotion,
            Rank = (uint)GetCurPathInfo().Rank,
            DressedSkinId = (uint)GetCurPathInfo().Skin
        };

        foreach (var item in GetCurPathInfo().Relic)
            proto.EquipRelicList.Add(new EquipRelic
            {
                RelicUniqueId = (uint)item.Value,
                Type = (uint)item.Key
            });

        if (GetCurPathInfo().EquipId != 0) proto.EquipmentUniqueId = (uint)GetCurPathInfo().EquipId;

        foreach (var skill in GetCurPathInfo().GetSkillTree())
            proto.SkilltreeList.Add(new AvatarSkillTree
            {
                PointId = (uint)skill.Key,
                Level = (uint)skill.Value
            });

        return proto;
    }

    public override LineupAvatar ToLineupInfo(int slot, LineupInfo info,
        AvatarType avatarType = AvatarType.AvatarFormalType)
    {
        return new LineupAvatar
        {
            Id = (uint)SpecialAvatarId,
            Slot = (uint)slot,
            AvatarType = avatarType,
            Hp = info.IsExtraLineup() ? (uint)ExtraLineupHp : (uint)CurrentHp,
            SpBar = new SpBarInfo
            {
                CurSp = info.IsExtraLineup() ? (uint)ExtraLineupSp : (uint)CurrentSp,
                MaxSp = 10000
            }
        };
    }

    public override BattleAvatar ToBattleProto(PlayerDataCollection collection,
        AvatarType avatarType = AvatarType.AvatarFormalType)
    {
        var proto = new BattleAvatar
        {
            Id = (uint)SpecialAvatarId,
            AvatarType = avatarType,
            Level = (uint)Level,
            Promotion = (uint)Promotion,
            Rank = (uint)GetCurPathInfo().Rank,
            Index = (uint)collection.LineupInfo.GetSlot(BaseAvatarId),
            Hp = (uint)GetCurHp(collection.LineupInfo.LineupType != 0),
            SpBar = new SpBarInfo
            {
                CurSp = (uint)GetCurSp(collection.LineupInfo.LineupType != 0),
                MaxSp = 10000
            },
            WorldLevel = (uint)collection.PlayerData.WorldLevel
        };

        foreach (var skill in GetCurPathInfo().GetSkillTree())
            proto.SkilltreeList.Add(new AvatarSkillTree
            {
                PointId = (uint)skill.Key,
                Level = (uint)skill.Value
            });

        foreach (var relic in GetCurPathInfo().Relic)
        {
            var item = collection.InventoryData.RelicItems?.Find(item => item.UniqueId == relic.Value);
            if (item != null)
            {
                var protoRelic = new BattleRelic
                {
                    Id = (uint)item.ItemId,
                    UniqueId = (uint)item.UniqueId,
                    Level = (uint)item.Level,
                    MainAffixId = (uint)item.MainAffix
                };

                if (item.SubAffixes.Count >= 1)
                    foreach (var subAffix in item.SubAffixes)
                        protoRelic.SubAffixList.Add(subAffix.ToProto());

                proto.RelicList.Add(protoRelic);
            }
        }

        if (GetCurPathInfo().EquipId != 0)
        {
            var item = collection.InventoryData.EquipmentItems.Find(item => item.UniqueId == GetCurPathInfo().EquipId);
            if (item != null)
                proto.EquipmentList.Add(new BattleEquipment
                {
                    Id = (uint)item.ItemId,
                    Level = (uint)item.Level,
                    Promotion = (uint)item.Promotion,
                    Rank = (uint)item.Rank
                });
        }
        else if (GetCurPathInfo().EquipData != null)
        {
            proto.EquipmentList.Add(new BattleEquipment
            {
                Id = (uint)GetCurPathInfo().EquipData!.ItemId,
                Level = (uint)GetCurPathInfo().EquipData!.Level,
                Promotion = (uint)GetCurPathInfo().EquipData!.Promotion,
                Rank = (uint)GetCurPathInfo().EquipData!.Rank
            });
        }

        return proto;
    }
}

public class OldAvatarInfo
{
    public int AvatarId { get; set; }

    public int PathId { get; set; }
    public int Level { get; set; }
    public int Exp { get; set; }
    public int Promotion { get; set; }
    public int Rewards { get; set; }
    public long Timestamp { get; set; }
    public int CurrentHp { get; set; } = 10000;
    public int CurrentSp { get; set; }
    public int ExtraLineupHp { get; set; } = 10000;
    public int ExtraLineupSp { get; set; }
    public bool IsMarked { get; set; } = false;
    public Dictionary<int, int> SkillTree { get; set; } = [];

    public Dictionary<int, Dictionary<int, int>> SkillTreeExtra { get; set; } =
        []; // for hero  heroId -> skillId -> level

    public Dictionary<int, PathInfo> PathInfoes { get; set; } = [];
}

public class PathInfo(int pathId)
{
    public int PathId { get; set; } = pathId;
    public int Skin { get; set; }
    public int Rank { get; set; }
    public int EquipId { get; set; } = 0;
    public Dictionary<int, int> Relic { get; set; } = [];
    public ItemData? EquipData { get; set; } // for special avatar
    public int EnhanceId { get; set; }
    public Dictionary<int, EnhanceInfo> EnhanceInfos { get; set; } = [];

    public Dictionary<int, int> GetSkillTree()
    {
        if (EnhanceInfos.TryGetValue(EnhanceId, out var enhance)) return enhance.SkillTree;

        EnhanceInfos[EnhanceId] = new EnhanceInfo(EnhanceId);

        // create default skill tree
        var avatarExcel = GameData.AvatarConfigData.GetValueOrDefault(PathId);
        if (avatarExcel == null) return [];

        var skills = avatarExcel.DefaultSkillTree.GetValueOrDefault(EnhanceId);
        if (skills == null) return [];

        foreach (var skill in skills) EnhanceInfos[EnhanceId].SkillTree.Add(skill.PointID, skill.Level);

        return EnhanceInfos[EnhanceId].SkillTree;
    }
}

public class EnhanceInfo(int enhanceId)
{
    public int EnhanceId { get; set; } = enhanceId;
    public Dictionary<int, int> SkillTree { get; set; } = [];
}

public record PlayerDataCollection(PlayerData PlayerData, InventoryData InventoryData, LineupInfo LineupInfo);
