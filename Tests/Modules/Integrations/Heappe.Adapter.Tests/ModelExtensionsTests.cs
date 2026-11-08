using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.HEappeApiDto.Responses;

using Userorg.Adapter.Heappe.Userorg;

namespace Heappe.Adapter.Tests;

public sealed class ModelExtensionsTests
{
    [Fact(DisplayName = "ConvertToModel ClusterInfoModel throws when null")]
    public void ConvertToModel_ClusterInfo_Null_Throws()
    {
        ClusterInfoModel? model = null;
        Assert.Throws<ArgumentNullException>(() => ModelExtensions.ConvertToModel(model!));
    }

    [Fact(DisplayName = "ConvertToModel NodeTypeModel throws when null")]
    public void ConvertToModel_NodeType_Null_Throws()
    {
        NodeTypeModel? model = null;
        Assert.Throws<ArgumentNullException>(() => ModelExtensions.ConvertToModel(model!));
    }

    [Fact(DisplayName = "ConvertToModel ProjectModel throws when null")]
    public void ConvertToModel_Project_Null_Throws()
    {
        ProjectModel? model = null;
        Assert.Throws<ArgumentNullException>(() => ModelExtensions.ConvertToModel(model!));
    }

    [Fact(DisplayName = "ConvertToModel CommandTemplateCompactModel throws when null")]
    public void ConvertToModel_CommandTemplate_Null_Throws()
    {
        CommandTemplateCompactModel? model = null;
        Assert.Throws<ArgumentNullException>(() => ModelExtensions.ConvertToModel(model!));
    }
}