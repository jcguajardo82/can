using System.Data;
using System.Data.SqlClient;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

using Soriana.PPS.Common.Extensions;
using Soriana.PPS.POS.PendingCancellation.Services;
using Soriana.PPS.DataAccess.Configuration;
using Soriana.PPS.Common.Data;
using Soriana.PPS.DataAccess.PaymentProcess;
using Soriana.PPS.DataAccess.Repository;

[assembly: FunctionsStartup(typeof(Soriana.PPS.POS.PendingCancellation.Startup))]
namespace Soriana.PPS.POS.PendingCancellation
{
    public class Startup : FunctionsStartup
    {
        #region Constructor
        public Startup() { }
        #endregion

        #region Overrides Methods
        public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
        {
            base.ConfigureAppConfiguration(builder);
        }

        public override void Configure(IFunctionsHostBuilder builder)
        {
            //Formatter Injection
            builder.Services.AddMvcCore().AddNewtonsoftJson(options => options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore);

            //Configuration Injection
            IConfiguration configuration = builder.GetContext().Configuration;
            builder.Services.Configure<IConfiguration>(configuration);

            //SeriLog Injection
            builder.Services.AddSeriLogConfiguration(configuration);

            //DataAccess Service Injection -- DataBase
            builder.Services.AddScoped<IDbConnection>(o =>
            {
                PaymentProcessorOptions paymentProcessorOptions = new PaymentProcessorOptions();
                configuration.GetSection(PaymentProcessorOptions.PAYMENT_PROCESSOR_OPTIONS_CONFIGURATION).Bind(paymentProcessorOptions);
                return new SqlConnection(paymentProcessorOptions.ConnectionString);
            });
            //DataAccess Service Injection -- Unit of Work
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>(o =>
            {
                return new UnitOfWork(o.GetRequiredService<IDbConnection>());
            });

            //DataAccess Service Injection -- Context
            builder.Services.AddScoped<IPaymentProcessContext, PaymentProcessContext>();

            //Business Service Injection -- Repository
            builder.Services.AddScoped<IClosurePaymentRepository, ClosurePaymentRepository>();

            //Business Service Injection -- Service
            builder.Services.AddScoped<IPendingCancellationService, PendingCancellationService>();

        }
        #endregion
    }
}
