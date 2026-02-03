using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Database.Lineup;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Scene; // 必须添加，AvatarSceneInfo 在这里
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using LineupInfo = EggLink.DanhengServer.Database.Lineup.LineupInfo;

namespace EggLink.DanhengServer.GameServer.Game.Battle.Custom;

public class BattleBoxingClubOptions(List<AvatarSceneInfo> customAvatars, List<uint> selectedBuffs, PlayerInstance player)
{
    public List<AvatarSceneInfo> CustomAvatars { get; set; } = customAvatars;
    public List<uint> SelectedBuffs { get; set; } = selectedBuffs;
    public PlayerInstance Player { get; set; } = player;

    public void HandleProto(SceneBattleInfo proto, BattleInstance battle)
    {
        // 1. 创建临时的编队上下文 (模仿 GridFight)
        // 这一步是为了让 ToBattleProto 内部计算属性时有正确的 Lineup 参考
        var tempLineup = new LineupInfo
        {
            BaseAvatars = CustomAvatars.Select(x => new LineupAvatarInfo
            {
                BaseAvatarId = x.AvatarInfo.BaseAvatarId,
                SpecialAvatarId = x.AvatarInfo is SpecialAvatarInfo s ? s.SpecialAvatarId : 0
            }).ToList(),
            LineupType = (int)ExtraLineupType.LineupBoxingClub
        };

        // 2. 注入阵容
        foreach (var avatarScene in CustomAvatars)
        {
            // 确保角色状态全满
            avatarScene.AvatarInfo.SetCurHp(10000, true);
            avatarScene.AvatarInfo.SetCurSp(10000, true);

            var battleAvatar = avatarScene.AvatarInfo.ToBattleProto(
                new PlayerDataCollection(Player.Data, Player.InventoryManager!.Data, tempLineup),
                avatarScene.AvatarType);
            
            // 按照选人顺序排列 (0, 1, 2, 3)
            battleAvatar.Index = (uint)CustomAvatars.IndexOf(avatarScene);
            proto.BattleAvatarList.Add(battleAvatar);
        }

        // 3. 注入搏击俱乐部共鸣增益
        foreach (var buffId in SelectedBuffs)
        {
            // 每一个 Resonance 其实就是一个全局 BattleBuff
            proto.BuffList.Add(new BattleBuff
            {
                Id = buffId,
                Level = 1,
                OwnerIndex = 0xFFFFFFFF // 官方常用此值表示全队收益
            });
        }
    }
}
