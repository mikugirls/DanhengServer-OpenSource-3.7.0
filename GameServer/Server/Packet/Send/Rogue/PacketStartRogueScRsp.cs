using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Rogue;

public class PacketStartRogueScRsp : BasePacket
{
    // 构造函数 1: 成功 (原版逻辑，不做修改，只显式加上 Retcode = 0)
    public PacketStartRogueScRsp(PlayerInstance player) : base(CmdIds.StartRogueScRsp)
    {
        var proto = new StartRogueScRsp
        {
            Retcode = 0,
            // 这里用回你原版的正确字段名
            RogueGameInfo = player.RogueManager!.ToProto(),
            Lineup = player.LineupManager!.GetCurLineup()!.ToProto(),
            Scene = player.SceneInstance!.ToProto()
        };

        // 使用基类自带的 SetData，无需手动引用 Google.Protobuf
        SetData(proto);
    }

    // 构造函数 2: 失败 (新增逻辑，用于 RogueManager 的 try-catch)
    public PacketStartRogueScRsp(uint retcode) : base(CmdIds.StartRogueScRsp)
    {
        var proto = new StartRogueScRsp
        {
            Retcode = retcode
        };

        SetData(proto);
    }
}