using EggLink.DanhengServer.GameServer.Server.Packet.Send.FightActivity; // 指向新目录
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Recv.FightActivity;

[Opcode(CmdIds.GetFightActivityDataCsReq)] // 3699
public class HandlerGetFightActivityDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        // 解析请求包
        var req = GetFightActivityDataCsReq.Parser.ParseFrom(data);

        // 调用刚才定义的静态响应包
        await connection.SendPacket(new PacketGetFightActivityDataScRsp());
        
        // 记录日志以便确认协议已触发
        // Logger.logger.Debug("[星芒战幕] 静态数据下发成功 (FightActivity 目录)");
    }
}
