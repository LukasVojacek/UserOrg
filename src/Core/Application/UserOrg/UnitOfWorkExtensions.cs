using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;

namespace UserOrganizationManagement.Application.UserOrg;

public static class UserOrgUnitOfWorkExtensions
{
    public static async Task<StatisticsModel> GetStatisticsFrom(this IUserOrgUnitOfWork uow, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        IList<WeekCount> weeklyUsers = await uow.UserRepository.CreatedFrom(fromDate, toDate, ct);
        var totalUsers = await uow.UserRepository.TotalCount(ct);

        IList<WeekCount> weeklyProjects = await uow.ProjectRepository.CreatedFrom(fromDate, toDate, ct);
        var totalProjects = await uow.ProjectRepository.TotalCount(ct);

        IList<WeekCount> weeklyResources = await uow.ProjectResourceRepository.CreatedFrom(fromDate, toDate, ct);
        var totalResources = await uow.ProjectResourceRepository.TotalCount(ct);

        IList<WeekPart> weeks = WeekPart.GetWeeksFromStartDate(fromDate, toDate);

        IEnumerable<WeeklyDataModel> weeklyModel = weeks.Select(w => new WeeklyDataModel(
            DateOnly.FromDateTime(w.StartDate),
            DateOnly.FromDateTime(w.EndDate),
            weeklyProjects.FirstOrDefault(p => p.From == DateOnly.FromDateTime(w.StartDate))?.Count ?? 0,
            weeklyUsers.FirstOrDefault(p => p.From == DateOnly.FromDateTime(w.StartDate))?.Count ?? 0,
            weeklyResources.FirstOrDefault(p => p.From == DateOnly.FromDateTime(w.StartDate))?.Count ?? 0));

        return new StatisticsModel(totalProjects, totalUsers, totalResources, weeklyModel.ToList());

    }
}