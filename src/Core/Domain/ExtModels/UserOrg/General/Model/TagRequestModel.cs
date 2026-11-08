using FluentValidation;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.General.Model;

public class TagRequestModel
{
    public string? TagName { get; set; }

    public TagRequestModel()
    {

    }

    public TagRequestModel(string? tagName)
    {
        TagName = tagName;
    }
}
public class TagRequestModelValidator : AbstractValidator<TagRequestModel>
{
    public TagRequestModelValidator()
    {
        RuleFor(x => x.TagName).MaximumLength(100);
    }
}