using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup;

public class PacketSetLineupNameScRsp : BasePacket
{
    // 按照你的风格，传入必要参数，然后在构造函数内 SetData
    public PacketSetLineupNameScRsp(uint index, string name, uint retcode = 0) : base(CmdIds.SetLineupNameScRsp)
    {
        // 1. 实例化生成的 Proto 对象
        var rsp = new SetLineupNameScRsp();

        // 2. 直接赋值：严格遵守从数据库/逻辑层传进来的值
        rsp.Index = index;
        rsp.Name = name ?? ""; 
        rsp.Retcode = retcode;

        // 3. 调用基类方法设置封包数据
        SetData(rsp);
    }
}