using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Sntp.Adept.Api;

/// <summary>
/// ФИО текущего пользователя: процедура SNTP_PPO dbo.__GetExecutersRoleExtended
/// даёт ExecutersID, ФИО читается из dogovors.dbo.EXECUTERS.Fio.
/// Так же резолвит подписанта SNTP-Крипто.
/// </summary>
public sealed class StaffDirectory(IConfiguration configuration, ILogger<StaffDirectory> logger)
{
    public string? CurrentFio()
    {
        var fromSql = TryExecuterFio();
        if (!string.IsNullOrWhiteSpace(fromSql)) return fromSql.Trim();
        return null;
    }

    public string WindowsAccount()
    {
        var name = Environment.UserName;
        var domain = Environment.UserDomainName;
        return string.IsNullOrWhiteSpace(domain) ? name : $"{domain}\\{name}";
    }

    private string? TryExecuterFio()
    {
        var connectionString = configuration.GetConnectionString("SNTP_PPO");
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            using var connection = new SqlConnection(connectionString);
            connection.Open();
            var parameters = new DynamicParameters();
            parameters.Add("@ExecutersID", dbType: DbType.Int32, direction: ParameterDirection.Output);
            parameters.Add("@MSG", dbType: DbType.String, size: 255, direction: ParameterDirection.Output);
            connection.Execute(
                "dbo.__GetExecutersRoleExtended",
                parameters,
                commandType: CommandType.StoredProcedure);
            var id = parameters.Get<int?>("@ExecutersID");
            if (id is not > 0) return null;
            return connection.QuerySingleOrDefault<string>(
                "SELECT LTRIM(RTRIM(Fio)) FROM dogovors.dbo.EXECUTERS WHERE Num = @Id",
                new { Id = id });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось прочитать ФИО текущего пользователя из EXECUTERS");
            return null;
        }
    }
}
