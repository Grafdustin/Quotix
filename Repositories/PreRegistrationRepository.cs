using Microsoft.Data.Sqlite;
using Quotix.Common;
using Quotix.Models;

namespace Quotix.Repositories;

/// <summary>
/// NDT 预报备单数据访问层。
/// </summary>
public class PreRegistrationRepository : BaseRepository
{
    public PreRegistrationRepository(DatabaseProvider db) : base(db) { }

    public List<PreRegistration> GetAll() =>
        Query("SELECT * FROM pre_registrations ORDER BY created_at DESC", null, Read);

    public PreRegistration? GetById(string id) =>
        Query("SELECT * FROM pre_registrations WHERE id = @id", new() { ["@id"] = id }, Read).FirstOrDefault();

    public void Insert(PreRegistration item)
    {
        Execute(@"
            INSERT INTO pre_registrations (
                id, title, agent_name, agent_sales, agent_phone, agent_email,
                middleman_name, middleman_address, middleman_sales, middleman_phone, middleman_email,
                customer_name, customer_tel, customer_address, customer_fax, customer_department,
                customer_email, customer_contact, customer_mobile, industry_market, information_source,
                application_purpose, recommended_products, competitor_info,
                activity_date1, activity_content1, activity_date2, activity_content2,
                activity_date3, activity_content3, activity_date4, activity_content4,
                activity_date5, activity_content5, activity_date6, activity_content6,
                case_result, registration_date, created_at, updated_at)
            VALUES (
                @id, @title, @agent_name, @agent_sales, @agent_phone, @agent_email,
                @middleman_name, @middleman_address, @middleman_sales, @middleman_phone, @middleman_email,
                @customer_name, @customer_tel, @customer_address, @customer_fax, @customer_department,
                @customer_email, @customer_contact, @customer_mobile, @industry_market, @information_source,
                @application_purpose, @recommended_products, @competitor_info,
                @activity_date1, @activity_content1, @activity_date2, @activity_content2,
                @activity_date3, @activity_content3, @activity_date4, @activity_content4,
                @activity_date5, @activity_content5, @activity_date6, @activity_content6,
                @case_result, @registration_date, @created_at, @updated_at)",
            Params(item));
    }

    public void Delete(string id) =>
        Execute("DELETE FROM pre_registrations WHERE id = @id", new() { ["@id"] = id });

    private static Dictionary<string, object?> Params(PreRegistration x) => new()
    {
        ["@id"] = x.Id,
        ["@title"] = x.Title,
        ["@agent_name"] = x.AgentName,
        ["@agent_sales"] = x.AgentSales,
        ["@agent_phone"] = x.AgentPhone,
        ["@agent_email"] = x.AgentEmail,
        ["@middleman_name"] = x.MiddlemanName,
        ["@middleman_address"] = x.MiddlemanAddress,
        ["@middleman_sales"] = x.MiddlemanSales,
        ["@middleman_phone"] = x.MiddlemanPhone,
        ["@middleman_email"] = x.MiddlemanEmail,
        ["@customer_name"] = x.CustomerName,
        ["@customer_tel"] = x.CustomerTel,
        ["@customer_address"] = x.CustomerAddress,
        ["@customer_fax"] = x.CustomerFax,
        ["@customer_department"] = x.CustomerDepartment,
        ["@customer_email"] = x.CustomerEmail,
        ["@customer_contact"] = x.CustomerContact,
        ["@customer_mobile"] = x.CustomerMobile,
        ["@industry_market"] = x.IndustryMarket,
        ["@information_source"] = x.InformationSource,
        ["@application_purpose"] = x.ApplicationPurpose,
        ["@recommended_products"] = x.RecommendedProducts,
        ["@competitor_info"] = x.CompetitorInfo,
        ["@activity_date1"] = x.ActivityDate1,
        ["@activity_content1"] = x.ActivityContent1,
        ["@activity_date2"] = x.ActivityDate2,
        ["@activity_content2"] = x.ActivityContent2,
        ["@activity_date3"] = x.ActivityDate3,
        ["@activity_content3"] = x.ActivityContent3,
        ["@activity_date4"] = x.ActivityDate4,
        ["@activity_content4"] = x.ActivityContent4,
        ["@activity_date5"] = x.ActivityDate5,
        ["@activity_content5"] = x.ActivityContent5,
        ["@activity_date6"] = x.ActivityDate6,
        ["@activity_content6"] = x.ActivityContent6,
        ["@case_result"] = x.CaseResult,
        ["@registration_date"] = x.RegistrationDate,
        ["@created_at"] = x.CreatedAt,
        ["@updated_at"] = x.UpdatedAt
    };

    private static PreRegistration Read(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Title = r.GetSafeStringOrEmpty("title"),
        AgentName = r.GetSafeStringOrEmpty("agent_name"),
        AgentSales = r.GetSafeStringOrEmpty("agent_sales"),
        AgentPhone = r.GetSafeStringOrEmpty("agent_phone"),
        AgentEmail = r.GetSafeStringOrEmpty("agent_email"),
        MiddlemanName = r.GetSafeStringOrEmpty("middleman_name"),
        MiddlemanAddress = r.GetSafeStringOrEmpty("middleman_address"),
        MiddlemanSales = r.GetSafeStringOrEmpty("middleman_sales"),
        MiddlemanPhone = r.GetSafeStringOrEmpty("middleman_phone"),
        MiddlemanEmail = r.GetSafeStringOrEmpty("middleman_email"),
        CustomerName = r.GetSafeStringOrEmpty("customer_name"),
        CustomerTel = r.GetSafeStringOrEmpty("customer_tel"),
        CustomerAddress = r.GetSafeStringOrEmpty("customer_address"),
        CustomerFax = r.GetSafeStringOrEmpty("customer_fax"),
        CustomerDepartment = r.GetSafeStringOrEmpty("customer_department"),
        CustomerEmail = r.GetSafeStringOrEmpty("customer_email"),
        CustomerContact = r.GetSafeStringOrEmpty("customer_contact"),
        CustomerMobile = r.GetSafeStringOrEmpty("customer_mobile"),
        IndustryMarket = r.GetSafeStringOrEmpty("industry_market"),
        InformationSource = r.GetSafeStringOrEmpty("information_source"),
        ApplicationPurpose = r.GetSafeStringOrEmpty("application_purpose"),
        RecommendedProducts = r.GetSafeStringOrEmpty("recommended_products"),
        CompetitorInfo = r.GetSafeStringOrEmpty("competitor_info"),
        ActivityDate1 = r.GetSafeStringOrEmpty("activity_date1"),
        ActivityContent1 = r.GetSafeStringOrEmpty("activity_content1"),
        ActivityDate2 = r.GetSafeStringOrEmpty("activity_date2"),
        ActivityContent2 = r.GetSafeStringOrEmpty("activity_content2"),
        ActivityDate3 = r.GetSafeStringOrEmpty("activity_date3"),
        ActivityContent3 = r.GetSafeStringOrEmpty("activity_content3"),
        ActivityDate4 = r.GetSafeStringOrEmpty("activity_date4"),
        ActivityContent4 = r.GetSafeStringOrEmpty("activity_content4"),
        ActivityDate5 = r.GetSafeStringOrEmpty("activity_date5"),
        ActivityContent5 = r.GetSafeStringOrEmpty("activity_content5"),
        ActivityDate6 = r.GetSafeStringOrEmpty("activity_date6"),
        ActivityContent6 = r.GetSafeStringOrEmpty("activity_content6"),
        CaseResult = r.GetSafeStringOrEmpty("case_result"),
        RegistrationDate = r.GetSafeStringOrEmpty("registration_date"),
        CreatedAt = r.GetSafeStringOrEmpty("created_at"),
        UpdatedAt = r.GetSafeStringOrEmpty("updated_at")
    };
}
