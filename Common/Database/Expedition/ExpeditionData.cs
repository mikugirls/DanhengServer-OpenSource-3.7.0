using EggLink.DanhengServer.Proto;
using SqlSugar;

namespace EggLink.DanhengServer.Database.Expedition;

[SugarTable("Expedition")] // 映射数据库表名
public class ExpeditionData : BaseDatabaseDataHelper
{
    // 使用 Json 格式存储派遣列表，方便扩展
    [SugarColumn(IsJson = true)] 
    public List<ExpeditionInfoInstance> ExpeditionList { get; set; } = [];

    public int TotalFinishedCount { get; set; }

    /// <summary>
    /// 将数据库模型转换为 Proto 协议列表
    /// </summary>
    public List<ExpeditionInfo> ToProto()
    {
        var protoList = new List<ExpeditionInfo>();

        foreach (var item in ExpeditionList)
        {
            var info = new ExpeditionInfo
            {
                Id = item.Id,
                TotalDuration = item.TotalDuration,
                StartExpeditionTime = item.StartExpeditionTime,
            };
            
            // 将存储的 uint 角色列表添加到 Proto 消息中
            info.AvatarIdList.AddRange(item.AvatarIdList);
            
            protoList.Add(info);
        }

        return protoList;
    }
}

public class ExpeditionInfoInstance
{
    public uint Id { get; set; }

    public uint TotalDuration { get; set; }

    public long StartExpeditionTime { get; set; }

    public List<uint> AvatarIdList { get; set; } = [];
}
