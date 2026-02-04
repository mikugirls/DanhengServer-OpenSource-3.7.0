using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Enums.Avatar;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup;

public class PacketGetLineupAvatarDataScRsp : BasePacket
{
    public PacketGetLineupAvatarDataScRsp(PlayerInstance player) : base(CmdIds.GetLineupAvatarDataScRsp)
    {
        var rsp = new GetLineupAvatarDataScRsp();

        // 1. 处理正式角色
        player.AvatarManager?.AvatarData?.FormalAvatars?.ForEach(avatar =>
        {
            rsp.AvatarDataList.Add(new LineupAvatarData
            {
                Id = (uint)avatar.BaseAvatarId,
                Hp = (uint)avatar.CurrentHp,
                AvatarType = AvatarType.AvatarFormalType
            });
        });

        // 2. 处理试用角色 (这是修复试用角色不显示的关键)
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
}
