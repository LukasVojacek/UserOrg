namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;

public sealed record StatisticsModel(int NumberOfProjects, int NumberOfUsers, int NumberOfResources, IList<WeeklyDataModel> WeeklyData)
{
    public static StatisticsModel Empty { get; } = new(0, 0, 0, []);
}
public sealed record WeeklyDataModel(DateOnly From, DateOnly To, int ProjectsCreated, int UsersCreated, int ResourcesCreated);

public sealed record WeeklyStatisticCount(int TotalCount, IList<WeekCount> WeeklyCounts);
public sealed record WeekCount(DateOnly From, DateOnly To, int Count);

public sealed record WeekPart(int Index, DateTime StartDate, DateTime EndDate)
{
    public static WeekPart Invalid { get; } = new(-1, DateTime.MinValue, DateTime.MinValue);
    public static WeekPart GetWeekPartForDate(IList<WeekPart> weeks, DateTime date) => weeks.FirstOrDefault(x => x.StartDate <= date && x.EndDate > date) ?? WeekPart.Invalid;
    public static IList<WeekPart> GetWeeksFromStartDate(DateOnly startDate, DateOnly endDate)
    {
        var index = 0;
        var startDateDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var weeks = new List<WeekPart>();

        DateTime nextMonday = startDateDateTime.AddDays(DayOfWeek.Monday - startDateDateTime.DayOfWeek);

        while (nextMonday <= DateTime.Today)
        {
            DateTime weekEnd = nextMonday.AddDays(6); // End of the week (Sunday)
            weeks.Add(new(index++, nextMonday, weekEnd));
            nextMonday = nextMonday.AddDays(7);
            if (nextMonday >= endDate.ToDateTime(TimeOnly.MaxValue))
            {
                break;
            }
        }

        return weeks;
    }
}