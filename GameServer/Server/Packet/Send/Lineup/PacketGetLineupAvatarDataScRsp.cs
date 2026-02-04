using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup;
public PacketGetLineupAvatarDataScRsp(PlayerInstance player) : base(CmdIds.GetLineupAvatarDataScRsp)
{
    var rsp = new GetLineupAvatarDataScRsp();

    // 处理正式角色
    player.AvatarManager?.AvatarData?.FormalAvatars?.ForEach(avatar =>
    {
        rsp.AvatarDataList.Add(new LineupAvatarData
        {
            Id = (uint)avatar.BaseAvatarId,
            Hp = (uint)avatar.CurrentHp,
            AvatarType = AvatarType.AvatarFormalType
        });
    });

    // 处理试用角色 (这是显示头像的关键)
    player.AvatarManager?.AvatarData?.TrialAvatars?.ForEach(trialAvatar =>
    {
        rsp.AvatarDataList.Add(new LineupAvatarData
        {
            Id = (uint)trialAvatar.SpecialAvatarId,
            Hp = (uint)trialAvatar.CurrentHp,
            AvatarType = AvatarType.AvatarTrialType
        });
    });

    SetData(rsp);
}
